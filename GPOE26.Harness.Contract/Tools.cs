namespace GPOE26.Harness.Contract;

/// <summary>
/// Les outils que le produit sait exposer. Des constantes, pas des chaînes libres : ce
/// sont des identifiants qu'une politique d'autorisation compare, et une faute de frappe
/// y ouvrirait ou fermerait un accès sans bruit.
/// </summary>
public static class ToolNames
{
    // ── Comprendre ────────────────────────────────────────────────────────────
    public const string ChercherDansLeCours = "chercher_dans_le_cours";
    public const string LireAVoixHaute = "lire_a_voix_haute";
    public const string OuEnEstLEleve = "ou_en_est_l_eleve";

    // ── S'exercer ─────────────────────────────────────────────────────────────
    public const string GenererExercice = "generer_exercice";
    public const string CorrigerReponse = "corriger_reponse";

    // ── Coder ─────────────────────────────────────────────────────────────────
    public const string LireFichier = "lire_fichier";
    public const string EcrireFichier = "ecrire_fichier";
    public const string ExecuterTests = "executer_tests";
}

/// <summary>Ce qu'un harness déclare d'un outil, pour que le modèle sache l'appeler.</summary>
/// <param name="Parameters">Schéma JSON des paramètres, tel qu'attendu par l'API du modèle.</param>
/// <param name="Mutating">
/// L'outil modifie-t-il quelque chose ? Sert au refus global des sessions de suivi : un
/// parent ne déclenche jamais d'écriture, même indirectement.
/// </param>
public record ToolDescriptor(
    string Name,
    string Description,
    string Parameters,
    bool Mutating = false
);

/// <summary>
/// Qui voit quel outil.
///
/// **Enregistrement ≠ exposition.** Un outil s'enregistre une fois au démarrage du
/// harness ; ce qu'une session en voit dépend de ce que l'élève est en train de faire et
/// de qui pose la question. C'est ce qui empêche un `executer_tests` d'être proposé
/// pendant une lecture, et tout outil quel qu'il soit d'être proposé à un parent.
///
/// Fonction pure et table de décision explicite, sur le modèle de
/// <c>StudyIdentity.CanViewStudent</c> : la règle doit être lisible d'un coup d'œil et
/// vérifiable sans réseau ni modèle. C'est aussi ce que la suite de conformité compare à
/// la réponse de <c>GET /outils</c> — la table est la loi, l'implémentation la suit.
/// </summary>
public static class ToolPolicy
{
    /// <summary>Outils d'une session de lecture : comprendre le cours, rien de plus.</summary>
    private static readonly string[] Lecture =
    [
        ToolNames.ChercherDansLeCours,
        ToolNames.LireAVoixHaute,
        ToolNames.OuEnEstLEleve,
    ];

    /// <summary>Lecture + de quoi produire et se faire corriger.</summary>
    private static readonly string[] Exercice =
    [
        .. Lecture,
        ToolNames.GenererExercice,
        ToolNames.CorrigerReponse,
    ];

    /// <summary>Exercice + l'espace de travail. Seule nature de session qui touche des fichiers.</summary>
    private static readonly string[] Code =
    [
        .. Exercice,
        ToolNames.LireFichier,
        ToolNames.EcrireFichier,
        ToolNames.ExecuterTests,
    ];

    /// <summary>
    /// Outils exposés à une session.
    ///
    /// Une session de suivi n'en reçoit aucun : un parent ou un enseignant consulte, il
    /// n'agit pas. Ce n'est pas une commodité d'affichage — un outil non exposé ne peut
    /// pas être appelé, quoi que le modèle décide.
    /// </summary>
    public static IReadOnlyList<string> For(SessionKind kind) => kind switch
    {
        SessionKind.Lecture => Lecture,
        SessionKind.Exercice => Exercice,
        SessionKind.Code => Code,
        SessionKind.Suivi => [],
        _ => [],
    };

    /// <summary>Cet outil est-il appelable dans cette nature de session ?</summary>
    public static bool Allows(SessionKind kind, string tool) =>
        For(kind).Contains(tool);

    /// <summary>
    /// Nature de session qu'un rôle a le droit d'ouvrir.
    ///
    /// Un parent ou un enseignant ne peut ouvrir qu'une session de suivi : lui laisser
    /// ouvrir une session de lecture au nom de son enfant reviendrait à lui donner le
    /// répétiteur de l'enfant, et donc à brouiller la frontière que la phase 2b a posée
    /// entre suivre et lire par-dessus l'épaule.
    /// </summary>
    public static bool CanOpen(string? role, SessionKind kind)
    {
        var isParentOrTeacher =
            string.Equals(role, "Parent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Teacher", StringComparison.OrdinalIgnoreCase);

        return isParentOrTeacher ? kind == SessionKind.Suivi : kind != SessionKind.Suivi;
    }
}
