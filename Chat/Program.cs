using Chat.Service;
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

// Choix du LLM via la config : "Claude" | "Gpt" | "Ollama" | "Foundry"
var provider = builder.Configuration["LlmProvider"] ?? "Claude";

builder.Services.AddSingleton<ILlmService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = sp.GetRequiredService<IHttpClientFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    switch (provider)
    {
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cours API V1"));
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
