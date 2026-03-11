using GPOE26.Web.Components;
using GPOE26.Web.Services;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// ── Token provider (shared entre AuthService et ApiClient) ─────
builder.Services.AddScoped<AuthTokenProvider>();

// ── HttpClients nommés (Aspire service discovery) ──────────────
builder.Services.AddHttpClient("user", client =>
    client.BaseAddress = new Uri("https+http://user"))
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("cours", client =>
    client.BaseAddress = new Uri("https+http://cours"))
    .AddStandardResilienceHandler();

// Chat et Quiz ont des timeouts longs (génération LLM = lent)
builder.Services.AddHttpClient("chat", client =>
{
    client.BaseAddress = new Uri("https+http://chat");
    client.Timeout = TimeSpan.FromMinutes(3); // Timeout HttpClient
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    });

builder.Services.AddHttpClient("quiz", client =>
{
    client.BaseAddress = new Uri("https+http://quiz");
    client.Timeout = TimeSpan.FromMinutes(5); // Timeout HttpClient
})
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
    });

builder.Services.AddHttpClient("apiservice", client =>
    client.BaseAddress = new Uri("https+http://apiservice"))
    .AddStandardResilienceHandler();

// ── Services métier ────────────────────────────────────────────
// IMPORTANT : Scoped = un par circuit Blazor Server = un par utilisateur
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();



var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseOutputCache();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

// ── Proxy pour les images uploadées sur l'ApiService ───────────
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
