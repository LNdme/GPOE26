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
builder.Services.AddTransient<JwtDelegatingHandler>();

// ── HttpClients nommés (Aspire service discovery) ──────────────
builder.Services.AddHttpClient("user", client =>
    client.BaseAddress = new Uri("https+http://user"));

builder.Services.AddHttpClient("cours", client =>
    client.BaseAddress = new Uri("https+http://cours"))
    .AddHttpMessageHandler<JwtDelegatingHandler>();

// Chat et Quiz ont des timeouts longs (génération LLM = lent)
builder.Services.AddHttpClient("chat", client =>
{
    client.BaseAddress = new Uri("https+http://chat");
    client.Timeout = TimeSpan.FromMinutes(3);
})
.AddHttpMessageHandler<JwtDelegatingHandler>();

builder.Services.AddHttpClient("quiz", client =>
{
    client.BaseAddress = new Uri("https+http://quiz");
    client.Timeout = TimeSpan.FromMinutes(3);
})
.AddHttpMessageHandler<JwtDelegatingHandler>();

builder.Services.AddHttpClient("apiservice", client =>
    client.BaseAddress = new Uri("https+http://apiservice"));

// ── Timeouts Polly pour les appels LLM (chat/quiz) ─────────────
builder.Services.Configure<HttpStandardResilienceOptions>("chat", o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
    o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
    o.Retry.MaxRetryAttempts = 1;
});
builder.Services.Configure<HttpStandardResilienceOptions>("quiz", o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
    o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
    o.Retry.MaxRetryAttempts = 1;
});

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
app.Run();
