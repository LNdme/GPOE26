namespace GPOE26.Ai;

/// <summary>
/// Configuration de la passerelle OpenRouter.
///
/// OpenRouter expose une API compatible OpenAI qui donne accès à de nombreux modèles
/// — dont des modèles vision et des modèles d'embeddings — derrière une seule clé.
/// C'est ce qui permet de choisir un modèle différent par agent sans multiplier les
/// fournisseurs ni les secrets.
///
/// La clé n'est JAMAIS écrite dans appsettings.json : elle arrive par la variable
/// d'environnement <c>OpenRouter__ApiKey</c>, injectée par l'AppHost Aspire.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>En-tête HTTP-Referer — identifie l'application dans les classements OpenRouter.</summary>
    public string? Referer { get; set; }

    /// <summary>En-tête X-Title — nom affiché de l'application côté OpenRouter.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Modèle utilisé quand aucun n'est configuré pour un agent donné.
    /// Doit être capable de vision pour que la transcription de photos fonctionne
    /// sans configuration explicite.
    /// </summary>
    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>
    /// Modèle par agent, indexé par le nom de <see cref="AgentKind"/>.
    /// Les identifiants exacts sont à choisir sur https://openrouter.ai/models —
    /// le catalogue évolue, d'où le passage par la configuration plutôt que par le code.
    /// </summary>
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Dimension des vecteurs produits par le modèle d'embedding.
    ///
    /// ⚠️ Cette valeur est figée dans le schéma PostgreSQL (colonne <c>vector(N)</c>).
    /// La changer impose une migration EF et une réindexation complète des cours.
    /// 1536 correspond à openai/text-embedding-3-small.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>Plafond de tokens en sortie, par défaut, pour les appels de rédaction.</summary>
    public int DefaultMaxTokens { get; set; } = 2048;

    /// <summary>
    /// Voix de synthèse. Les identifiants dépendent du modèle choisi pour
    /// <see cref="AgentKind.Voix"/> — voir la fiche du modèle sur openrouter.ai.
    /// </summary>
    public string Voice { get; set; } = "alloy";

    /// <summary>
    /// Longueur maximale d'un texte envoyé à la synthèse vocale.
    ///
    /// Le TTS se facture au caractère : une garde évite qu'un collage malencontreux
    /// n'envoie un cours entier à lire.
    /// </summary>
    public int MaxSpeechCharacters { get; set; } = 2_000;

    public string ModelFor(AgentKind kind) =>
        Models.TryGetValue(kind.ToString(), out var model) && !string.IsNullOrWhiteSpace(model)
            ? model
            : DefaultModel;
}
