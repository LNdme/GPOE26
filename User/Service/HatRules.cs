using User.Model;

namespace User.Service;

/// <summary>
/// Casquettes d'une personne (docs/PLAN-PUBLICS.md, lot P3) : toute personne est apprenante, et peut en plus suivre
/// des enfants, enseigner, gérer un espace ou payer pour d'autres. Elles remplacent peu à peu le rôle unique
/// (UserRole), qui reste en base comme casquette par défaut. Règles pures, testées sans base.
/// </summary>
public static class HatRules
{
    public const string Learner = "learner";
    public const string Guardian = "guardian";
    public const string Teacher = "teacher";
    public const string Manager = "manager";
    public const string Payer = "payer";

    /// <summary>Ce que la base dit de la personne, en plus de son rôle.</summary>
    /// <param name="FollowsChild">Lien parent ↔ enfant actif, du côté du parent.</param>
    /// <param name="TeachesInSpace">Membre actif d'un espace avec le rôle Teacher.</param>
    /// <param name="ManagesSpace">Responsable (Owner) ou gestionnaire (Manager) actif d'un espace.</param>
    /// <param name="PaysForFamily">Formule Famille en cours ou en attente de paiement, dont la personne est le payeur.</param>
    public record Facts(bool FollowsChild = false, bool TeachesInSpace = false, bool ManagesSpace = false, bool PaysForFamily = false);

    /// <summary>Casquettes, dans un ordre stable : apprenant d'abord.</summary>
    public static IReadOnlyList<string> Compute(UserRole role, Facts facts)
    {
        var hats = new List<string> { Learner };
        if (role == UserRole.Parent || facts.FollowsChild) hats.Add(Guardian);
        if (role == UserRole.Teacher || facts.TeachesInSpace) hats.Add(Teacher);
        if (facts.ManagesSpace) hats.Add(Manager);
        if (facts.PaysForFamily) hats.Add(Payer);
        return hats;
    }

    /// <summary>
    /// Casquettes déduites du seul rôle, pour un jeton émis avant le lot P3 (sans claim « hats ») : même résultat
    /// que <see cref="Compute"/> sans rien savoir de plus.
    /// </summary>
    public static IReadOnlyList<string> FromRole(UserRole role) => Compute(role, new Facts());
}
