namespace GPOE26.Ai;

/// <summary>Un tour de conversation transmis au modèle. <paramref name="Role"/> vaut "user" ou "assistant".</summary>
public readonly record struct AgentTurn(string Role, string Content)
{
    public static AgentTurn User(string content) => new("user", content);
    public static AgentTurn Assistant(string content) => new("assistant", content);
}

/// <summary>
/// Une image transmise à un modèle vision. Les octets sont encodés en data URI
/// (<c>data:image/jpeg;base64,…</c>) au moment de l'appel : c'est le format attendu
/// par le champ <c>image_url</c> de l'API compatible OpenAI.
/// </summary>
public sealed record AgentImage(byte[] Bytes, string MediaType)
{
    public string ToDataUri() => $"data:{MediaType};base64,{Convert.ToBase64String(Bytes)}";

    /// <summary>Déduit le type MIME d'après l'extension du fichier, ou null si non supporté.</summary>
    public static string? MediaTypeForExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };
}

/// <summary>Une requête de complétion adressée à un agent.</summary>
public sealed record AgentRequest
{
    public required AgentKind Kind { get; init; }

    public string? SystemPrompt { get; init; }

    public IReadOnlyList<AgentTurn> Turns { get; init; } = [];

    /// <summary>Images jointes au dernier tour utilisateur (modèles vision uniquement).</summary>
    public IReadOnlyList<AgentImage> Images { get; init; } = [];

    public int? MaxTokens { get; init; }

    /// <summary>
    /// Demande au modèle de ne répondre qu'avec un objet JSON valide.
    /// Utilisé par le Routeur et le Relecteur, dont la sortie est parsée.
    /// </summary>
    public bool JsonMode { get; init; }
}

/// <summary>Métadonnées d'un modèle exposé par OpenRouter (pour <c>GET /chat/models</c>).</summary>
public sealed record AgentModelInfo(string Id, string? Name, string? Description, int? ContextLength);

/// <summary>Levée quand OpenRouter refuse ou échoue une requête, avec le corps de réponse.</summary>
public sealed class OpenRouterException(string message) : Exception(message);
