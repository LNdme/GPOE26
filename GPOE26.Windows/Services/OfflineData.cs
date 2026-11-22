using System.Net;
using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Desktop.Services;

/// <summary>
/// Résultat d'une lecture : la valeur, et d'où elle vient. <see cref="SavedAt"/> renseigné = copie locale
/// (le serveur n'a pas répondu) ; l'interface l'affiche (« Hors connexion — copie du 12 nov. à 14:05 »).
/// </summary>
public sealed record Loaded<T>(T? Value, string? Error, DateTime? SavedAt = null)
{
    public bool FromCache => SavedAt is not null;
}

/// <summary>État de la connexion aux services, partagé par toute l'application.</summary>
public sealed class Connectivity
{
    public bool IsOnline { get; private set; } = true;

    /// <summary>Déclenché quand l'application passe en ligne ou hors connexion.</summary>
    public event Action<bool>? Changed;

    public void Report(bool online)
    {
        if (online == IsOnline) return;
        IsOnline = online;
        Changed?.Invoke(online);
    }

    /// <summary>Le serveur n'a pas répondu (réseau coupé, service arrêté) — par opposition à un refus (403, 404).</summary>
    public static bool IsNetworkFailure(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout;
}

/// <summary>
/// Accès aux données de l'élève « réseau d'abord, copie locale ensuite » :
/// chaque réponse reçue est enregistrée ; si le serveur est injoignable, on relit la dernière copie.
/// <see cref="SyncAsync"/> télécharge d'avance tous les cours et toutes les discussions, pour que l'élève
/// retrouve son travail hors connexion même sur un cours qu'il n'a pas ouvert depuis l'application.
/// </summary>
public sealed class OfflineData(LocalStore store, ICourseApi courses, ITutorApi tutor, IAssessmentApi assessments,
    IProgressApi progress, IAgentApi agent, AppSession app, Connectivity net)
{
    public const string CoursesKey = "courses";
    public static string CourseKey(Guid id) => $"course:{id}";
    public static string ConversationKey(Guid courseId) => $"tutor:{courseId}";

    /// <summary>Mes cours et ceux partagés avec moi (classes, bibliothèque), sans doublon.</summary>
    public Task<Loaded<List<CourseSummary>>> CoursesAsync() => LoadAsync(CoursesKey, async () =>
    {
        var mine = await courses.MineAsync();
        if (!mine.Ok) return mine;
        var shared = await courses.SharedWithMeAsync();
        if (!shared.Ok) return shared;
        var all = (mine.Value ?? []).Concat(shared.Value ?? [])
            .GroupBy(c => c.Id).Select(g => g.First())
            .OrderBy(c => c.Subject).ThenBy(c => c.Title)
            .ToList();
        return new ApiResult<List<CourseSummary>>(all, null, HttpStatusCode.OK);
    });

    public Task<Loaded<CourseDetail>> CourseAsync(Guid id) => LoadAsync(CourseKey(id), () => courses.GetAsync(id));

    public Task<Loaded<TutorConversation>> ConversationAsync(Guid courseId) =>
        LoadAsync(ConversationKey(courseId), () => tutor.ConversationAsync(courseId));

    // ─── Outils de la barre de droite : mêmes règles (réseau d'abord, copie locale ensuite) ───

    public Task<Loaded<List<TutorConversationSummary>>> ConversationsAsync() => LoadAsync("tutor:list", tutor.ConversationsAsync);
    public Task<Loaded<List<PinnedExplanation>>> PinnedAsync() => LoadAsync("pinned", tutor.PinnedAsync);
    public Task<Loaded<DayPlan>> TodayPlanAsync() => LoadAsync("plan:today", agent.TodayPlanAsync);
    public Task<Loaded<StudentProgress>> ProgressAsync() => LoadAsync("progress", progress.MineAsync);
    public Task<Loaded<List<StudentAssessment>>> StudentAssessmentsAsync() => LoadAsync("assessments:student", assessments.MineAsStudentAsync);
    public Task<Loaded<List<Assessment>>> TeacherAssessmentsAsync() => LoadAsync("assessments:teacher", assessments.MineAsTeacherAsync);
    public Task<Loaded<List<AgentMemoryItem>>> MemoriesAsync() => LoadAsync("memories", agent.MemoriesAsync);

    /// <summary>Travail fait dans l'application (dernier quiz, dernière série d'exercices) : gardé sur ce poste seulement.</summary>
    public async Task KeepAsync<T>(string key, T value)
    {
        if (app.UserId is { } user) await store.PutAsync(user, key, value);
    }

    public async Task<StoredValue<T>?> KeptAsync<T>(string key) =>
        app.UserId is { } user ? await store.GetAsync<T>(user, key) : null;

    /// <summary>Après un échange avec le tuteur : la discussion à jour part dans la copie locale.</summary>
    public async Task SaveConversationAsync(TutorConversation conversation)
    {
        if (app.UserId is { } user)
            await store.PutAsync(user, ConversationKey(conversation.CourseId), conversation);
    }

    /// <summary>
    /// Télécharge d'avance tous les cours et leurs discussions. Trois requêtes à la fois au plus,
    /// pour ne pas saturer une connexion lente. Renvoie le nombre de cours copiés.
    /// </summary>
    public async Task<int> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (app.IsReadOnly) return 0;
        var list = await CoursesAsync();
        if (list.FromCache || list.Value is null) return 0;

        var done = 0;
        await Parallel.ForEachAsync(list.Value, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (course, _) =>
        {
            await CourseAsync(course.Id);
            await ConversationAsync(course.Id);
            var n = Interlocked.Increment(ref done);
            progress?.Report($"Copie locale : {n}/{list.Value.Count} cours");
        });
        // Les outils aussi, pour les ouvrir hors connexion (un refus selon le rôle est simplement ignoré)
        await Task.WhenAll(ConversationsAsync(), PinnedAsync(), MemoriesAsync());
        if (app.Profile?.Role == UserRole.Student)
            await Task.WhenAll(StudentAssessmentsAsync(), ProgressAsync(), TodayPlanAsync());
        else if (app.Profile?.Role == UserRole.Teacher)
            await TeacherAssessmentsAsync();
        await store.SetSettingAsync($"last-sync:{app.UserId}", DateTime.UtcNow.ToString("O"));
        return done;
    }

    public async Task<DateTime?> LastSyncAsync() =>
        await store.GetSettingAsync($"last-sync:{app.UserId}") is { } text && DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)
            ? at : null;

    private async Task<Loaded<T>> LoadAsync<T>(string key, Func<Task<ApiResult<T>>> fetch)
    {
        if (app.UserId is not { } user) return new Loaded<T>(default, "Connecte-toi pour voir tes cours.");

        if (!app.IsReadOnly)
        {
            var result = await fetch();
            if (result.Ok)
            {
                net.Report(true);
                if (result.Value is not null) await store.PutAsync(user, key, result.Value);
                return new Loaded<T>(result.Value, null);
            }
            if (!Connectivity.IsNetworkFailure(result.Status))
                return new Loaded<T>(default, result.Error);
            net.Report(false);
        }

        var saved = await store.GetAsync<T>(user, key);
        return saved is null
            ? new Loaded<T>(default, "Hors connexion, et ce contenu n'a pas encore été copié sur ce poste.")
            : new Loaded<T>(saved.Value, null, saved.SavedAt);
    }
}
