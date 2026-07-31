using System.Text;
using Chat.Agents;
using Chat.Data;
using Chat.Service;
using GPOE26.Ai;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ─── Authentification ─────────────────────────────────────────────────────────
//
// Le service appelait UseAuthorization() sans jamais installer d'authentification :
// aucun endpoint ne pouvait donc être réellement protégé, et le JWT de l'élève
// n'était pas lu. Or le répétiteur en a besoin — pour identifier l'élève, et pour
// relayer son jeton au service Cours, qui filtre les cours sur leur propriétaire.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "votre_cle_secrete_tres_longue_et_aleatoire_ici_changez_moi_en_production_cle_256_bits";

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
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ─── Historique d'étude ───────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<ChatContext>("chatdb");

// ─── Accès au service Cours ───────────────────────────────────────────────────
//
// Référence à sens unique : Chat lit les cours, jamais l'inverse. Les agents de
// traitement de contenu (transcription, structuration) vivent dans la bibliothèque
// partagée GPOE26.Ai, ce qui évite d'avoir aussi besoin de Chat depuis Cours — et
// donc une référence circulaire entre les deux services Aspire.
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(CoursClient.HttpClientName, client =>
    client.BaseAddress = new Uri("https+http://cours"));

builder.Services.AddScoped<CoursClient>();

// ─── Passerelle IA + agents ───────────────────────────────────────────────────
builder.Services.AddGpoeAi(builder.Configuration);

// Cache des audios de synthèse vocale. La taille est exprimée en octets d'audio
// (voir l'entrée posée par /chat/voix) : ~64 Mo, soit largement de quoi couvrir les
// explications réécoutées d'une session, sans laisser le cache grossir sans fin.
builder.Services.AddMemoryCache(options => options.SizeLimit = 64L * 1024 * 1024);

builder.Services.AddScoped<RouteurAgent>();
builder.Services.AddScoped<RetrieverAgent>();
builder.Services.AddScoped<TuteurAgent>();
builder.Services.AddScoped<ExerciceAgent>();
builder.Services.AddScoped<RelecteurAgent>();
builder.Services.AddScoped<CorrecteurAgent>();
builder.Services.AddScoped<MemoireAgent>();
builder.Services.AddScoped<RepetiteurOrchestrator>();

// Client HTTP des implémentations ILlmService historiques (DeepSeek, GPT, Foundry).
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
        Title = "Chat API",
        Description = "Répétiteur multi-agents : routage, recherche dans le cours, tutorat",
        Version = "v1"
    });
});

// ─── Choix du fournisseur pour les endpoints historiques ──────────────────────
//
// "OpenRouter" par défaut : une seule clé donne accès au chat, à la vision et aux
// embeddings, et le modèle se choisit agent par agent. Les autres implémentations
// restent disponibles par configuration.
var provider = builder.Configuration["LlmProvider"] ?? "OpenRouter";

builder.Services.AddScoped<ILlmService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = sp.GetRequiredService<IHttpClientFactory>();

    return provider switch
    {
        "Gpt" => new GptService(config, http),
        "Foundry" => new FoundryService(config, http),
        "Ollama" or "FallbackChain" or "DeepSeek" => new DeepSeekService(config, http),
        "Claude" => new ClaudeService(config),
        _ => new OpenRouterService(
            sp.GetRequiredService<OpenRouterClient>(),
            sp.GetRequiredService<CoursClient>()),
    };
});

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Chat API V1"));
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
