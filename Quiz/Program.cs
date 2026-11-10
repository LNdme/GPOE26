using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quiz.Data;
using Quiz.Endpoints;
using Quiz.Service;
using Microsoft.Extensions.Http.Resilience;
using GPOE26.ServiceDefaults.Llm;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Stockage en mémoire (singleton pour partager l'état entre requêtes)
builder.Services.AddSingleton<IQuizStore, InMemoryQuizStore>();

// Service de génération via LLM
builder.Services.AddScoped<IQuizGeneratorService, OpenAiQuizGeneratorService>();

// ─── Évaluations des enseignants (base PostgreSQL) ────────────────────────────
var quizConnStr = builder.Configuration.GetConnectionString("QuizDB")
    ?? throw new InvalidOperationException("No connection string for QuizDB");

builder.Services.AddDbContext<EvaluationContext>(options => options.UseNpgsql(quizConnStr));
builder.Services.AddScoped<ProgressService>();

// Correction assistée et lecture des copies photographiées (LLM compatible OpenAI, section "LLM")
builder.AddLlmClient();
builder.Services.AddScoped<AiGradingService>();
builder.Services.AddScoped<AiQuota>();
builder.Services.AddSingleton<TranscriptionQueue>();
builder.Services.AddHostedService<TranscriptionWorker>();

// Service Cours : texte du cours dont on génère les exercices (JWT de l'utilisateur transmis)
builder.Services.AddHttpClient<CourseTextClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Cours"] ?? "http://localhost:5188");
    client.Timeout = TimeSpan.FromSeconds(15);
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
var jwtKey = GPOE26.ServiceDefaults.JwtSigningKey.Require(builder.Configuration, builder.Environment);

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
    catch (Exception ex) when (ex is UserServiceUnavailableException or CoursServiceUnavailableException && !context.Response.HasStarted)
    {
        app.Logger.LogError(ex.InnerException, "{Message}", ex.Message);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message + " Réessayez dans un instant." });
    }
});

// En développement, les services s'appellent en HTTP avec un JWT : une redirection vers HTTPS
// ferait retirer l'en-tête Authorization par HttpClient, et chaque appel finirait en 401.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapAssessmentEndpoints();
app.MapSubmissionEndpoints();
app.MapProgressEndpoints();

app.Run();
