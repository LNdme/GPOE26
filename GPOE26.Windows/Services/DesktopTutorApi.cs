using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Modèles d'IA proposés par le serveur pour le tuteur (ordre de repli), texte et lecture de photos,
/// et la taille de la fenêtre de contexte de chacun, en tokens, quand le fournisseur la publie.
/// </summary>
public sealed record TutorModels(List<string> Text, List<string> Vision, Dictionary<string, int>? Context = null);

/// <summary>
/// Appels du tuteur propres à l'application Windows : la liste des modèles et la question avec le modèle choisi.
/// Le reste (discussion, photos, épinglage) passe par <see cref="ITutorApi"/> de GPOE26.UI, comme sur le site.
/// </summary>
public sealed class DesktopTutorApi(IHttpClientFactory factory, SessionState session) : ApiBase(factory, session, ApiClients.Chat)
{
    public Task<ApiResult<TutorModels>> ModelsAsync() => GetAsync<TutorModels>("/tutor/models");

    /// <param name="model">Modèle choisi dans le sélecteur ; null = « Automatique » (ordre du serveur).</param>
    public Task<ApiResult<TutorExchange>> AskStreamAsync(Guid courseId, string? message, Guid? imageId, string? model,
        Func<string, Task> onDelta, CancellationToken ct = default) =>
        PostStreamAsync<TutorExchange>($"/tutor/courses/{courseId}/messages/stream", new { message, imageId, model }, onDelta, ct);
}
