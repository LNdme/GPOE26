using System.Text;
using Chat.Data;
using Chat.Endpoints;
using Chat.Service;
using GPOE26.ServiceDefaults.Llm;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("LlmClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
        options.Retry.MaxRetryAttempts = 1;
    });




builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cours API",
        Description = "Gestion des cours — texte et PDF",
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

// Services Cours (contenu du cours) et User (abonnement), appelés avec le JWT de l'élève
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("cours", c => c.BaseAddress = new Uri(builder.Configuration["Services:Cours"] ?? "http://localhost:5188"));
builder.Services.AddHttpClient("user", c => c.BaseAddress = new Uri(builder.Configuration["Services:User"] ?? "http://localhost:5197"));
builder.Services.AddScoped<PlatformClients>();

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
            RoleClaimType = "role",
            NameClaimType = "username"
        };
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();

// Choix du LLM des anciens écrans via la config : "OpenRouter" | "DeepSeek" | "Claude" | "Gpt" | "Foundry"
var provider = builder.Configuration["LlmProvider"] ?? "OpenRouter";

builder.Services.AddScoped<ILlmService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = sp.GetRequiredService<IHttpClientFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    switch (provider)
    {
        case "OpenRouter":
        case "OpenAiCompatible":
            return new CompatibleLlmService(sp.GetRequiredService<LlmClient>());
        case "Gpt":
            return new GptService(config, http);
        case "Foundry":
            return new FoundryService(config, http);
        case "Ollama": // Remplacé par DeepSeek comme demandé
        case "FallbackChain":
        case "DeepSeek":
            return new DeepSeekService(config, http);
        default:
            return new ClaudeService(config);
    }
});

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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cours API V1"));
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapTutorEndpoints();

app.Run();
