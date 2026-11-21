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
    [Inject] private IAccountApi Account { get; set; } = default!;

    /// <summary>Rôles autorisés (null = tout utilisateur connecté). Ancien contrôle : préférer <see cref="RequiredHats"/>.</summary>
    protected virtual UserRole[]? AllowedRoles => null;

    /// <summary>
    /// Casquettes dont l'une suffit pour ouvrir la page (UserHats ; null = tout utilisateur connecté). Lot P3 :
    /// un parent porte aussi la casquette « learner » et peut donc apprendre.
    /// </summary>
    protected virtual string[]? RequiredHats => null;

    /// <summary>Vrai pour la seule page ouverte à un élève connecté avec le code de sa fiche, qui doit d'abord choisir son mot de passe.</summary>
    protected virtual bool AllowsPendingPassword => false;

    protected bool Ready { get; private set; }

    // Garde le dernier profil connu : un 401 peut fermer la session juste avant la redirection vers /login
    private UserProfile? _me;
    protected UserProfile Me => _me = Session.Profile ?? _me!;

    protected override async Task OnInitializedAsync()
    {
        await Session.RestoreAsync();
        if (!Session.IsAuthenticated)
        {
            var back = Nav.ToBaseRelativePath(Nav.Uri);
            Nav.NavigateTo($"/login?retour={Uri.EscapeDataString("/" + back)}");
            return;
        }
        // Droits à jour avant d'afficher la page : un abonnement activé depuis la connexion déverrouille le tuteur
        await Session.RefreshProfileAsync(async () => (await Account.MeAsync()).Value);
        if (!Session.IsAuthenticated) return;   // jeton refusé pendant la relecture : la mise en page renvoie vers /login
        if (Session.Profile?.MustChangePassword == true && !AllowsPendingPassword)
        {
            var back = Nav.ToBaseRelativePath(Nav.Uri);
            Nav.NavigateTo($"/app/mot-de-passe?retour={Uri.EscapeDataString("/" + back)}");
            return;
        }
        if ((AllowedRoles is { } roles && !roles.Contains(Session.Role!.Value))
            || (RequiredHats is { } hats && !hats.Any(Session.HasHat)))
        {
            Nav.NavigateTo(HomeFor(Session.Role));
            return;
        }
        Ready = true;
        await LoadAsync();
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>
    /// Accueil d'un rôle : toujours l'espace /app, qui s'adapte au rôle (plan d'études, classes, suivi des enfants).
    /// Un administrateur garde son rôle et son espace ; il rejoint l'administration par le menu.
    /// </summary>
    public static string HomeFor(UserRole? role) => "/app";

    /// <summary>Adresse de retour sûre (chemin local uniquement, jamais un autre site), sinon l'accueil du rôle.</summary>
    public static string SafeReturnUrl(string? url, UserRole? role = null) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : HomeFor(role);
}
