using GPOE26.Web.Components;
using GPOE26.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// ── HttpClients nommés (Aspire service discovery) ──────────────
builder.Services.AddHttpClient("user");             // Auth API
builder.Services.AddHttpClient("cours");            // Cours API
builder.Services.AddHttpClient("chat");             // Chat IA API
builder.Services.AddHttpClient("quiz");             // Quiz API
builder.Services.AddHttpClient("GPOE26ApiService"); // API principale (news, events, contact...)

// ── Services métier ────────────────────────────────────────────
// IMPORTANT : les deux sont Scoped (= par circuit Blazor Server = par utilisateur)
// Un Singleton NE PEUT PAS dépendre d'un Scoped → on garde tout en Scoped
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
