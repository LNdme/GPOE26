namespace GPOE26.Harness.Contract;

/// <summary>Une entrée du sommaire, telle que la produit le pipeline Markdown.</summary>
/// <param name="Level">Niveau de titre : 2 pour un `##`, 3 pour un `###`.</param>
/// <param name="Anchor">Identifiant de l'ancre dans le HTML, pour le défilement.</param>
public record OutlineEntryDto(int Level, string Title, string Anchor);

/// <summary>
/// Un cours rendu, prêt à afficher.
///
/// Le Web et l'application bureau montrent le même cours : le rendre deux fois, c'est
/// garantir qu'ils divergeront. Le harness le rend une fois, avec le pipeline Markdig
/// existant, et les deux surfaces se contentent d'afficher. La feuille de style
/// (<c>wwwroot/css/reading.css</c>) et le suivi de lecture
/// (<c>wwwroot/js/reading-interop.js</c>) sont déjà indépendants de Blazor et se
/// transportent tels quels.
/// </summary>
/// <param name="Html">
/// HTML confié à un conteneur de lecture. Produit par Markdig avec <c>DisableHtml()</c> :
/// le Markdown d'un cours vient d'un modèle ou d'un élève, on ne lui laisse pas injecter
/// de balises.
/// </param>
/// <param name="RenderedAt">
/// Date du rendu, pour que l'application bureau sache si son cache local est périmé.
/// </param>
public record RenderedCourseDto(
    Guid CourseId,
    string Title,
    string Subject,
    string Html,
    IReadOnlyList<OutlineEntryDto> Outline,
    DateTime RenderedAt
);

// ── Synchronisation hors ligne ────────────────────────────────────────────────

/// <summary>
/// Nature d'un signal d'activité.
///
/// ⚠️ L'ordre doit rester identique à <c>Cours.Model.StudyActivity</c> : les enums
/// transitent en nombres entre les services.
/// </summary>
public enum StudyActivityKind { Lecture, Question, Etape, Exercice }

/// <summary>
/// Un signal d'activité, **horodaté par son émetteur**.
///
/// ⚠️ C'est la différence qui rend le hors ligne possible. Aujourd'hui
/// <c>StudySessionService.RecordAsync</c> calcule le temps écoulé depuis
/// <c>DateTime.UtcNow</c> : rejouer au matin une séance de la veille lui ferait compter
/// toute la nuit. Le signal doit porter l'heure à laquelle il a eu lieu ; la règle des
/// 30 minutes et le plafond de 90 secondes s'appliquent alors à cette heure-là.
/// </summary>
public record ActivitySignal(
    Guid CourseId,
    StudyActivityKind Kind,
    DateTime OccurredAt
);

/// <summary>
/// Un résultat d'étape produit hors ligne.
/// </summary>
/// <param name="SelfAssessed">
/// Le verdict vient-il de l'appareil de l'élève ? Un test exécuté en local est
/// falsifiable : on ne le présente donc jamais au parent comme une note vérifiée. Mieux
/// vaut une mesure honnêtement étiquetée qu'un chiffre faux.
/// </param>
public record OfflineStepResult(
    Guid CourseId,
    Guid StepId,
    int Score,
    int Total,
    DateTime CompletedAt,
    IReadOnlyList<string>? WeakHeadings = null,
    string? StudentAnswer = null,
    string? CorrectionSummary = null,
    bool SelfAssessed = false
);

/// <summary>
/// Le lot que l'application bureau remonte au retour du réseau.
///
/// Idempotent par construction : chaque élément porte sa date, et le serveur rejoue un
/// lot déjà reçu sans effet supplémentaire. Une synchronisation qu'on n'ose pas relancer
/// est une synchronisation qui finit par perdre des données.
/// </summary>
public record SyncBatch(
    IReadOnlyList<ActivitySignal> Activities,
    IReadOnlyList<OfflineStepResult> Results
);

/// <summary>Ce que la synchronisation a retenu, pour que le client purge sa file.</summary>
public record SyncResult(
    int ActivitiesAccepted,
    int ResultsAccepted,
    IReadOnlyList<string> Rejected
);
