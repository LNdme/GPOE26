namespace GPOE26.Harness.Contract;

/// <summary>
/// Ce que l'élève est en train de faire. Détermine les outils exposés — voir
/// <see cref="ToolPolicy"/>.
///
/// ⚠️ Les enums transitent en NOMBRES entre les services (aucun JsonStringEnumConverter
/// n'est enregistré dans ce projet). L'ordre est donc porteur de sens : on ajoute à la
/// fin, on ne réordonne jamais.
/// </summary>
public enum SessionKind
{
    /// <summary>Lecture d'un cours : comprendre, faire expliquer, se faire lire à voix haute.</summary>
    Lecture,

    /// <summary>Exercice rédigé : produire, se faire corriger.</summary>
    Exercice,

    /// <summary>Exercice de code : écrire, exécuter, lire les échecs, recommencer.</summary>
    Code,

    /// <summary>Consultation par un parent ou un enseignant. Lecture seule, sans exception.</summary>
    Suivi,
}

/// <summary>Ouverture d'une session d'étude.</summary>
/// <param name="StudentId">
/// L'élève concerné. Omis, c'est l'appelant. Renseigné par un parent ou un enseignant,
/// il est vérifié contre le lien famille ou la classe — un identifiant quelconque ne
/// suffit pas à obtenir une session.
/// </param>
public record OpenSessionRequest(
    Guid CourseId,
    SessionKind Kind,
    Guid? StudentId = null
);

/// <summary>
/// État d'une session.
/// </summary>
/// <param name="Turns">Nombre de tours déjà joués — utile pour savoir si la compaction approche.</param>
/// <param name="CompactedAt">Dernière compaction du contexte, s'il y en a eu une.</param>
public record SessionDto(
    Guid Id,
    Guid StudentId,
    Guid CourseId,
    SessionKind Kind,
    DateTime StartedAt,
    DateTime LastActivityAt,
    int Turns,
    DateTime? CompactedAt,
    IReadOnlyList<string> Tools
);

/// <summary>Une question posée dans une session.</summary>
/// <param name="Message">Ce que l'élève écrit.</param>
/// <param name="Context">
/// État courant que l'agent doit voir sans le demander : partie du cours affichée,
/// fichiers ouverts, dernière sortie de test. Facultatif, et volontairement libre — c'est
/// à chaque nature de session de décider ce qui la concerne.
/// </param>
public record TurnRequest(
    string Message,
    IReadOnlyDictionary<string, string>? Context = null
);

/// <summary>Un tour passé, tel que le renvoie la reprise de session.</summary>
public record TurnDto(
    int Index,
    string Role,
    string Content,
    DateTime At,
    IReadOnlyList<string>? Citations = null
);

/// <summary>Historique d'une session, pour la reprendre là où elle s'est arrêtée.</summary>
public record SessionHistoryDto(
    SessionDto Session,
    IReadOnlyList<TurnDto> Turns
);
