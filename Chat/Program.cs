using Chat.Service;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

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

    return provider switch
    {
        "Gpt" => (ILlmService)new GptService(config, http),
        "Ollama" => new OllamaService(config, http),
        "Foundry" => new FoundryService(config, http),
        _ => new ClaudeService(config)  // défaut
    };
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