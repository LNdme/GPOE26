using Quiz.Service;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Stockage en mémoire (singleton pour partager l'état entre requêtes)
builder.Services.AddSingleton<IQuizStore, InMemoryQuizStore>();

// Service de génération via LLM
builder.Services.AddHttpClient<IQuizGeneratorService, OpenAiQuizGeneratorService>();

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