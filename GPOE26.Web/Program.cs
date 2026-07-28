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

// ── Proxy pour les documents de cours (PDF et photos) servis par le service Cours ──
//
// Sans cette route, PdfPath/CourseAsset ne sont atteignables par aucun navigateur :
// le service Cours n'est pas exposé publiquement. C'est ce qui permet à l'onglet
// « Document original » du canvas de lecture d'afficher le PDF ou les photos.
//
// NOTE sécurité : comme le proxy images existant, cette route est anonyme. Les
// chemins sont des GUID non devinables, mais ce n'est pas un contrôle d'accès.
// Un vrai cloisonnement demanderait une authentification par cookie (le JWT vit
// dans un service scoped du circuit Blazor, inaccessible à une requête <iframe>).
app.MapGet("/uploads/cours/{*path}", async (string path, IHttpClientFactory factory) =>
{
    // Le chemin est réinjecté dans une URL : refuser toute tentative de remontée.
    if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        return Results.NotFound();

    var client = factory.CreateClient("cours");
    var response = await client.GetAsync(
        $"/uploads/cours/{path}", HttpCompletionOption.ResponseHeadersRead);

    if (!response.IsSuccessStatusCode)
    {
        response.Dispose();
        return Results.NotFound();
    }

    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

    // Recopie dans le corps de la réponse via le callback : la réponse HTTP amont est
    // ainsi libérée une fois le transfert terminé. Un PDF de cours pèse jusqu'à 10 Mo —
    // laisser la connexion ouverte jusqu'au passage du GC, comme le fait le proxy images,
    // épuiserait le pool de connexions.
    return Results.Stream(async output =>
    {
        using (response)
        await using (var input = await response.Content.ReadAsStreamAsync())
        {
            await input.CopyToAsync(output);
        }
    }, contentType);
});

app.Run();
