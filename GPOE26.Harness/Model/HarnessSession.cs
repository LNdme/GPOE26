using GPOE26.Harness.Contract;

namespace GPOE26.Harness.Model;

/// <summary>
/// Une session d'étude, persistée.
///
/// « Les sessions comme infrastructure » est ce que Hermes fait de mieux : une session
/// survit à la fermeture d'un onglet, se reprend, et se compacte quand elle s'allonge.
/// C'est aussi ce qui rendra le hors ligne possible — une session reprise depuis
/// l'application bureau est la même que celle laissée sur le Web.
/// </summary>
public class HarnessSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }
    public SessionKind Kind { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>Dernière compaction du contexte, s'il y en a eu une.</summary>
    public DateTime? CompactedAt { get; set; }

    /// <summary>
    /// Résumé des tours effacés par une compaction.
    ///
    /// On ne jette pas un historique : on le remplace par ce qu'il faut en retenir. Sans
    /// ce résumé, une session longue perdrait le fil au moment précis où elle en a le
    /// plus besoin.
    /// </summary>
    public string? Summary { get; set; }

    public List<HarnessTurn> Turns { get; set; } = [];
}

/// <summary>Un tour de la conversation.</summary>
public class HarnessTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public HarnessSession Session { get; set; } = null!;

    public int Index { get; set; }

    /// <summary>« user » ou « assistant ».</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>Sections du cours citées, séparées par « | ».</summary>
    public string? Citations { get; set; }

    /// <summary>
    /// Outils appelés pendant ce tour, séparés par « | ».
    ///
    /// Conservés pour la mesure, pas pour l'affichage : c'est ce qui permettra de dire
    /// après coup combien d'allers-retours une question a coûté.
    /// </summary>
    public string? ToolsUsed { get; set; }

    /// <summary>Compacté : le contenu a été résumé dans la session et n'est plus rejoué au modèle.</summary>
    public bool Compacted { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
}
