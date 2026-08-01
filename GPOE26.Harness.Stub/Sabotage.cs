namespace GPOE26.Harness.Stub;

/// <summary>
/// Défauts que le stub sait introduire volontairement, sur demande.
///
/// Une suite de tests qui ne tombe jamais ne démontre rien : elle peut être verte parce
/// que le service est correct, ou parce qu'elle ne vérifie rien. Le seul moyen de les
/// distinguer est de casser le service exprès et d'exiger l'échec.
///
/// Chaque valeur correspond à une garantie de l'étage conformité. Si l'un de ces
/// sabotages laisse la suite verte, c'est la suite qui a un défaut, pas le stub.
///
/// Activation : variable d'environnement <c>STUB_SABOTAGE</c>.
/// </summary>
public enum Sabotage
{
    /// <summary>Comportement conforme.</summary>
    None,

    /// <summary>Expose un outil que la table n'autorise pas dans cette nature de session.</summary>
    ExtraTool,

    /// <summary>Glisse le contenu d'un échange dans une réponse de suivi.</summary>
    LeakMessage,

    /// <summary>Termine le flux sans évènement `done` ni `error`.</summary>
    NoTerminalEvent,

    /// <summary>Laisse un parent consulter n'importe quel élève.</summary>
    SkipAuthorization,

    /// <summary>Oublie l'historique : une session reprise revient vide.</summary>
    ForgetHistory,

    /// <summary>Avale l'échec d'un outil au lieu d'émettre `error`.</summary>
    SwallowToolFailure,
}

public static class SabotageSettings
{
    public const string EnvironmentVariable = "STUB_SABOTAGE";

    /// <summary>
    /// Lit le sabotage demandé. Une valeur inconnue est refusée bruyamment : un nom mal
    /// orthographié donnerait un stub conforme et une suite verte, c'est-à-dire
    /// exactement la fausse assurance qu'on cherche à éviter.
    /// </summary>
    public static Sabotage Current
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value)) return Sabotage.None;

            if (!Enum.TryParse<Sabotage>(value, ignoreCase: true, out var sabotage))
                throw new InvalidOperationException(
                    $"{EnvironmentVariable}='{value}' n'est pas un sabotage connu. " +
                    $"Valeurs acceptées : {string.Join(", ", Enum.GetNames<Sabotage>())}.");

            return sabotage;
        }
    }

    public static bool Is(Sabotage sabotage) => Current == sabotage;
}
