namespace GPOE26.Ai;

/// <summary>
/// Les rôles distincts que le produit confie à un LLM.
///
/// Chaque rôle est associé à un modèle dans la configuration (<see cref="OpenRouterOptions.Models"/>) :
/// c'est ce qui permet d'utiliser plusieurs LLM en parallèle — un modèle vision bon marché
/// pour lire les photos, un modèle solide pour rédiger, un petit modèle pour classer.
/// </summary>
public enum AgentKind
{
    /// <summary>Transcription des photos de cours (nécessite un modèle vision).</summary>
    Vision,

    /// <summary>Réécriture du cours brut en Markdown pédagogique structuré.</summary>
    Structurateur,

    /// <summary>Classification de l'intention de l'élève.</summary>
    Routeur,

    /// <summary>Reformulation de la question pour la recherche vectorielle.</summary>
    Retriever,

    /// <summary>Réponse pédagogique ancrée sur les passages du cours.</summary>
    Tuteur,

    /// <summary>Génération d'exercices ciblés et de leur corrigé.</summary>
    Exercice,

    /// <summary>Vérification de la fidélité de la réponse au cours.</summary>
    Relecteur,

    /// <summary>Tenue de la fiche de suivi de l'élève.</summary>
    Memoire,

    /// <summary>Vectorisation du cours et des questions.</summary>
    Embedding,

    /// <summary>Lecture à voix haute des explications (synthèse vocale).</summary>
    Voix,

    /// <summary>
    /// Bilan rédigé pour un parent.
    ///
    /// Rôle distinct du tuteur : le destinataire n'est pas l'élève, n'a pas suivi le
    /// cours, et lit un texte qui peut avoir des conséquences à la maison. Cela mérite
    /// un modèle qu'on choisit pour sa qualité de rédaction, pas pour sa pédagogie.
    /// </summary>
    Bilan,
}
