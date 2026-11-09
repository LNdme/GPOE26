using GPOE26.Web.Components;
using GPOE26.UI.Services;
using GPOE26.Web.Services;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddScoped<AuthTokenProvider>();

var apiServiceUrl = builder.Configuration["Services:ApiService"] ?? "http://localhost:5455";
var userServiceUrl = builder.Configuration["Services:User"] ?? "http://localhost:5197";
var coursServiceUrl = builder.Configuration["Services:Cours"] ?? "http://localhost:5188";
var chatServiceUrl = builder.Configuration["Services:Chat"] ?? "http://localhost:5212";
var quizServiceUrl = builder.Configuration["Services:Quiz"] ?? "http://localhost:5163";

builder.Services.AddHttpClient("user", client =>
    client.BaseAddress = new Uri(userServiceUrl))
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("cours", client =>
    client.BaseAddress = new Uri(coursServiceUrl))
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("chat", client =>
{
    client.BaseAddress = new Uri(chatServiceUrl);
    client.Timeout = TimeSpan.FromMinutes(3);
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    });

builder.Services.AddHttpClient("quiz", client =>
{
    client.BaseAddress = new Uri(quizServiceUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    });

builder.Services.AddHttpClient("apiservice", client =>
    client.BaseAddress = new Uri(apiServiceUrl))
    .AddStandardResilienceHandler();

builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();

// ─── Espace d'apprentissage (bibliothèque GPOE26.UI, partagée avec l'application Windows) ──
// Session conservée dans le localStorage du navigateur (chiffrée par la protection des données ASP.NET)
builder.Services.AddGpoeLearningUi();
builder.Services.AddScoped<ISessionStore, BrowserSessionStore>();
AppTime.Zone = AppTime.Find(builder.Configuration["App:TimeZone"] ?? "Africa/Douala");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.UseOutputCache();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(GPOE26.UI.Components.AppPage).Assembly);   // pages de l'espace d'apprentissage
app.MapDefaultEndpoints();

app.MapGet("/uploads/images/{*path}", async (string path, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("apiservice");
    var response = await client.GetAsync($"/uploads/images/{path}");
    if (response.IsSuccessStatusCode)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return Results.Stream(stream, contentType);
    }
    return Results.NotFound();
});

app.Run();
