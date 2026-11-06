using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.AspNetCore.Components;

namespace GPOE26.UI.Components;

/// <summary>
/// Base des pages de l'espace connecté : restaure la session, renvoie vers /login si besoin
/// (avec retour à la page demandée), et vers /app si le rôle n'a pas accès à la page.
/// Les pages redéfinissent LoadAsync et n'affichent leur contenu qu'une fois Ready.
/// </summary>
public abstract class AppPage : ComponentBase
{
    [Inject] protected SessionState Session { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    /// <summary>Rôles autorisés (null = tout utilisateur connecté)</summary>
    protected virtual UserRole[]? AllowedRoles => null;

    protected bool Ready { get; private set; }

    protected UserProfile Me => Session.Profile!;

    protected override async Task OnInitializedAsync()
    {
        await Session.RestoreAsync();
        if (!Session.IsAuthenticated)
        {
            var back = Nav.ToBaseRelativePath(Nav.Uri);
            Nav.NavigateTo($"/login?retour={Uri.EscapeDataString("/" + back)}");
            return;
        }
        if (AllowedRoles is { } roles && !roles.Contains(Session.Role!.Value))
        {
            Nav.NavigateTo("/app");
            return;
        }
        Ready = true;
        await LoadAsync();
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Adresse de retour sûre (chemin local uniquement, jamais un autre site).</summary>
    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/app";
}
