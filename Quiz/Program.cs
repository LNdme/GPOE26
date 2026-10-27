using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quiz.Data;
using Quiz.Endpoints;
using Quiz.Service;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Stockage en mémoire (singleton pour partager l'état entre requêtes)
builder.Services.AddSingleton<IQuizStore, InMemoryQuizStore>();

// Service de génération via LLM
builder.Services.AddHttpClient<IQuizGeneratorService, OpenAiQuizGeneratorService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
        options.Retry.MaxRetryAttempts = 1;
    });

// ─── Évaluations des enseignants (base PostgreSQL) ────────────────────────────
var quizConnStr = builder.Configuration.GetConnectionString("QuizDB")
    ?? throw new InvalidOperationException("No connection string for QuizDB");

builder.Services.AddDbContext<EvaluationContext>(options => options.UseNpgsql(quizConnStr));
builder.Services.AddScoped<ProgressService>();

// Correction assistée par l'IA (même LLM que la génération de quiz)
builder.Services.AddHttpClient<AiGradingService>(client => client.Timeout = TimeSpan.FromMinutes(2))
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        options.Retry.MaxRetryAttempts = 1;
    });

// Service User : classes, élèves et rattachements (le JWT de l'appelant est transmis)
var userServiceUrl = builder.Configuration["Services:User"] ?? "http://localhost:5197";
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<UserDirectoryClient>(client => client.BaseAddress = new Uri(userServiceUrl))
    .AddStandardResilienceHandler();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddValidation();

// ─── JWT (émis par le service User) ───────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("La configuration Jwt:Key est manquante.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // Les claims gardent leur nom court du JWT ("sub", "role"…)
            RoleClaimType = "role",
            NameClaimType = "username"
        };
        options.MapInboundClaims = false;
    });
builder.Services.AddRolePolicies();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await EvaluationSchema.EnsureAsync(scope.ServiceProvider.GetRequiredService<EvaluationContext>());
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Le service User est indispensable aux contrôles d'accès : s'il ne répond pas → 503 explicite
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (UserServiceUnavailableException ex)
    {
        app.Logger.LogError(ex.InnerException, "Service User indisponible");
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapAssessmentEndpoints();
app.MapSubmissionEndpoints();
app.MapProgressEndpoints();

app.Run();
