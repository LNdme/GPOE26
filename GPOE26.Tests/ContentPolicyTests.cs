using System.Security.Claims;
using GPOE26.ApiService;
using User.Model;

namespace GPOE26.Tests;

/// <summary>
/// Qui a le droit de modifier le site.
///
/// Ces règles sont nouvelles parce que, jusqu'ici, il n'y en avait aucune : le service
/// de contenu portait dix-huit endpoints d'écriture et un dépôt de fichiers sans la
/// moindre authentification. Ce fichier fige la fermeture de cette porte.
///
/// Comme <c>StudyIdentity.CanViewStudent</c>, ce sont des fonctions pures — pas de
/// réseau, pas de base — et c'est précisément ce qui donne leur valeur à ces tests :
/// une autorisation trop large ne produit aucun message d'erreur, juste un inconnu qui
/// publie sur le site du lycée.
/// </summary>
public class ContentPolicyTests
{
    private static ClaimsPrincipal Avec(string role) =>
        new(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", role)],
            authenticationType: "Test"));   // ← rend l'identité authentifiée

    /// <summary>Un jeton absent : le visiteur anonyme, l'état d'avant cette étape.</summary>
    private static ClaimsPrincipal Anonyme() => new(new ClaimsIdentity());

    // ── Rédaction ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Admin")]
    [InlineData("Editor")]
    public void Un_redacteur_et_un_admin_publient(string role)
    {
        Assert.True(ContentPolicies.CanPublish(Avec(role)));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Parent")]
    public void Les_autres_roles_ne_publient_pas(string role)
    {
        Assert.False(ContentPolicies.CanPublish(Avec(role)));
    }

    // ── Administration ────────────────────────────────────────────────────────

    [Fact]
    public void Seul_un_administrateur_gouverne()
    {
        Assert.True(ContentPolicies.CanAdminister(Avec("Admin")));
    }

    /// <summary>
    /// La distinction qui justifie d'avoir deux niveaux plutôt qu'un : un rédacteur
    /// alimente le site, il ne touche ni à l'équipe de direction, ni aux coordonnées,
    /// ni aux comptes.
    /// </summary>
    [Fact]
    public void Un_redacteur_ne_gouverne_pas()
    {
        var redacteur = Avec("Editor");

        Assert.True(ContentPolicies.CanPublish(redacteur));
        Assert.False(ContentPolicies.CanAdminister(redacteur));
    }

    // ── L'anonyme, c'est-à-dire l'état d'avant ────────────────────────────────

    /// <summary>
    /// Le test qui compte le plus. Avant cette étape, cet appelant-là réussissait tout.
    /// </summary>
    [Fact]
    public void Un_anonyme_ne_peut_rien()
    {
        var anonyme = Anonyme();

        Assert.False(ContentPolicies.CanPublish(anonyme));
        Assert.False(ContentPolicies.CanAdminister(anonyme));
    }

    /// <summary>
    /// Un rôle non authentifié ne vaut rien : porter le mot « Admin » dans une identité
    /// que personne n'a signée ne donne aucun droit.
    /// </summary>
    [Fact]
    public void Un_claim_Admin_sans_authentification_ne_donne_rien()
    {
        var bricole = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Admin")]));

        Assert.False(bricole.Identity?.IsAuthenticated ?? false);
        Assert.False(ContentPolicies.CanAdminister(bricole));
    }

    [Fact]
    public void Un_role_inconnu_est_refuse()
    {
        Assert.False(ContentPolicies.CanPublish(Avec("Superviseur")));
        Assert.False(ContentPolicies.CanAdminister(Avec("")));
    }

    /// <summary>La casse du rôle ne doit pas décider d'une autorisation.</summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    public void La_casse_du_role_est_sans_effet(string role)
    {
        Assert.True(ContentPolicies.CanAdminister(Avec(role)));
    }

    // ── Les deux services disent-ils la même chose ? ──────────────────────────

    /// <summary>
    /// <c>ContentPolicies</c> recopie les noms de rôles plutôt que de référencer le
    /// service User — ce service de contenu n'en dépend pas, et lui donner cette
    /// dépendance pour deux chaînes coûterait plus que cela ne rapporte.
    ///
    /// Le prix de cette copie, c'est ce test : il échoue le jour où un rôle est renommé
    /// d'un côté seulement, au lieu de laisser l'autorisation diverger en silence.
    /// </summary>
    [Fact]
    public void Les_noms_de_roles_concordent_entre_les_deux_services()
    {
        Assert.Equal(
            new[] { nameof(UserRole.Admin), nameof(UserRole.Editor) }.OrderBy(r => r),
            ContentPolicies.Redacteurs.OrderBy(r => r));

        Assert.Equal(
            new[] { nameof(UserRole.Admin) },
            ContentPolicies.Administrateurs.ToArray());
    }
}

/// <summary>
/// Le verrou de l'inscription : un visiteur ne se décerne pas un rôle de pouvoir en
/// remplissant un formulaire.
/// </summary>
public class SelfAssignableRoleTests
{
    [Theory]
    [InlineData(UserRole.Student)]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Parent)]
    public void Les_roles_ordinaires_restent_choisissables(UserRole role)
    {
        Assert.Contains(role, RolePermissions.SelfAssignable);
    }

    /// <summary>
    /// Sans ce refus, « role: "Admin" » dans le corps de la requête d'inscription
    /// suffirait à prendre la main sur le site, l'équipe de direction et les comptes.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Editor)]
    public void Les_roles_de_pouvoir_ne_sont_pas_choisissables(UserRole role)
    {
        Assert.DoesNotContain(role, RolePermissions.SelfAssignable);
    }

    /// <summary>
    /// La liste est blanche, et non noire : un rôle ajouté à l'énumération dans six mois
    /// sera refusé par défaut plutôt qu'autorisé par oubli. Ce test échoue dès qu'un
    /// rôle apparaît sans qu'on ait tranché de quel côté il tombe.
    /// </summary>
    [Fact]
    public void Tout_nouveau_role_est_refuse_par_defaut()
    {
        var connus = new[] { UserRole.Student, UserRole.Teacher, UserRole.Parent,
                             UserRole.Admin, UserRole.Editor };

        Assert.Equal(connus.OrderBy(r => r), Enum.GetValues<UserRole>().OrderBy(r => r));

        foreach (var role in Enum.GetValues<UserRole>().Except(connus))
            Assert.DoesNotContain(role, RolePermissions.SelfAssignable);
    }

    [Fact]
    public void Les_deux_tables_de_permission_concordent()
    {
        Assert.True(RolePermissions.CanPublish(UserRole.Admin));
        Assert.True(RolePermissions.CanPublish(UserRole.Editor));
        Assert.False(RolePermissions.CanPublish(UserRole.Teacher));

        Assert.True(RolePermissions.CanAdminister(UserRole.Admin));
        Assert.False(RolePermissions.CanAdminister(UserRole.Editor));
    }
}
