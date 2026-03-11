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

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
