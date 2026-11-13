using System.Text;
using Chat.Data;
using Chat.Endpoints;
using Chat.Service;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddOpenApi();




builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Chat API",
        Description = "Tuteur IA, assistant enseignant et mémoire des agents",
        Version = "v1"
    });
    // Indiquer à Swagger que les endpoints nécessitent un Bearer token

});

// ─── Tuteur IA de l'espace d'apprentissage : discussions conservées en base ───
var chatConnStr = builder.Configuration.GetConnectionString("ChatDB")
    ?? throw new InvalidOperationException("No connection string for ChatDB");
builder.Services.AddDbContext<TutorContext>(options => options.UseNpgsql(chatConnStr));

// LLM compatible OpenAI (OpenRouter par défaut, modèles gratuits avec repli) : section "LLM"
builder.AddLlmClient();

// Services Cours (contenu du cours), User (abonnement) et Quiz (progression), appelés avec le JWT de l'élève.
// Délai court : un service lent ne doit pas bloquer le tuteur (le délai par défaut de HttpClient est de 100 s).
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
var platformTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Services:TimeoutSeconds", 10));
builder.Services.AddHttpClient("cours", c => { c.BaseAddress = new Uri(builder.Configuration["Services:Cours"] ?? "http://localhost:5188"); c.Timeout = platformTimeout; });
builder.Services.AddHttpClient("user", c => { c.BaseAddress = new Uri(builder.Configuration["Services:User"] ?? "http://localhost:5197"); c.Timeout = platformTimeout; });
builder.Services.AddHttpClient("quiz", c => { c.BaseAddress = new Uri(builder.Configuration["Services:Quiz"] ?? "http://localhost:5163"); c.Timeout = platformTimeout; });
builder.Services.AddScoped<PlatformClients>();
builder.Services.AddScoped<AiQuota>();
builder.Services.AddHostedService<TutorImageCleanup>();

// Mémoire des agents : relue à chaque question, consolidée en arrière-plan après chaque échange
builder.Services.AddScoped<AgentMemoryStore>();
builder.Services.AddScoped<AgentContextService>();
builder.Services.AddScoped<CourseIndex>();   // passages des cours pour le tuteur (H12)
builder.Services.AddSingleton<AgentReflectionQueue>();
builder.Services.AddHostedService<AgentReflectionWorker>();
builder.Services.AddHostedService<TutorReflectionScheduler>();

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
            RoleClaimType = "role",
            NameClaimType = "username"
        };
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();

// Brouillon de cours (/chat/draft) : même client LLM que le tuteur. Les anciens services (Claude, GPT, DeepSeek,
// Gemini, Foundry, Ollama…) et le réglage LlmProvider ont été retirés : plus aucun écran ne les utilisait.
builder.Services.AddScoped<ILlmService, CompatibleLlmService>();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await TutorSchema.EnsureAsync(scope.ServiceProvider.GetRequiredService<TutorContext>());
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Chat API V1"));
}

// Un service dont dépend la réponse ne répond pas : 503 et un message honnête (pas « réservé aux abonnés »)
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (PlatformUnavailableException ex) when (!context.Response.HasStarted)
    {
        app.Logger.LogWarning(ex.InnerException, "Service indisponible : {Message}", ex.Message);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});
// En développement, les services s'appellent en HTTP avec un JWT : une redirection vers HTTPS
// ferait retirer l'en-tête Authorization par HttpClient, et chaque appel finirait en 401.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapTutorEndpoints();
app.MapAgentEndpoints();
app.MapInsightEndpoints();

app.Run();
