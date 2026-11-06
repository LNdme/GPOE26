using System.Net;
using System.Text.Json;
using GPOE26.UI.Models;
using GPOE26.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GPOE26.Offline.Tests;

public class OfflineTests : IAsyncDisposable
{
    private readonly FakeServer _server = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gpoe-offline-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    private static readonly Guid StudentId = Guid.NewGuid();
    private static readonly Guid AssessmentId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid OpenQuestion = Guid.NewGuid();
    private static readonly Guid ChoiceQuestion = Guid.NewGuid();

    private class MemoryStore : ISessionStore
    {
        private StoredSession? _s;
        public Task<StoredSession?> LoadAsync() => Task.FromResult(_s);
        public Task SaveAsync(StoredSession session) { _s = session; return Task.CompletedTask; }
        public Task ClearAsync() { _s = null; return Task.CompletedTask; }
    }

    public OfflineTests()
    {
        var services = new ServiceCollection();
        foreach (var name in new[] { ApiClients.User, ApiClients.Cours, ApiClients.Quiz, ApiClients.Chat })
            services.AddHttpClient(name, c => c.BaseAddress = new Uri("http://fake")).ConfigurePrimaryHttpMessageHandler(() => _server);
        services.AddGpoeLearningUi();
        services.AddSingleton<ISessionStore, MemoryStore>();
        services.AddGpoeOffline(new OfflineOptions { DataDirectory = _dir, ProbeInterval = TimeSpan.FromMilliseconds(100) });
        _root = services.BuildServiceProvider();
        _scope = _root.CreateAsyncScope();
    }

    private T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    private async Task SignInAsync(DateTime? expires = null)
    {
        var profile = new UserProfile(StudentId, "e@t.cm", "Élève", UserRole.Student, "Terminale", null, null, "fr", DateTime.UtcNow,
            null, null, null, null, null, null, null, null, null, null, true, null, null);
        await Get<SessionState>().SignInAsync(new AuthResponse("jeton", expires ?? DateTime.UtcNow.AddHours(1), profile));
    }

    private OfflinePackage Package(int attemptsLeft = 2) => new(AssessmentId, "Devoir MRUV", null, AssessmentKind.Homework, "Physique",
        null, DateTime.UtcNow.AddDays(3), 30, attemptsLeft, 5, 20,
        [new AttemptQuestion(ChoiceQuestion, QuestionType.SingleChoice, "L'accélération est…", 1, [new(0, "nulle"), new(1, "constante")], null),
         new AttemptQuestion(OpenQuestion, QuestionType.OpenEnded, "Calculer la distance.", 4, [], null)],
        DateTime.UtcNow, DateTime.UtcNow);

    private SubmissionReview Review(Guid id) => new(id, AssessmentId, "Devoir MRUV", StudentId, 1, SubmissionStatus.Submitted,
        DateTime.UtcNow, DateTime.UtcNow, false, false, null, 5, null, 20, [], Offline: true);

    private static StudentAssessment Listed(int used = 0) => new(AssessmentId, "Devoir MRUV", null, AssessmentKind.Homework,
        AssessmentStatus.Published, "Physique", null, DateTime.UtcNow.AddDays(3), 30, 3, used, false, 2, 5, true, null, 20);

    private static ImageUpload Photo() => new("copie.jpg", "image/jpeg", [0xFF, 0xD8, 1, 2, 3]);

    [Fact]
    public async Task Un_cours_lu_en_ligne_reste_lisible_hors_connexion()
    {
        await SignInAsync();
        var course = new CourseDetail(CourseId, "MRUV", "Physique", null, CourseContentType.Text, "x = ½at²", null, [], DateTime.UtcNow,
            DateTime.UtcNow, CourseStatus.Published, [], Guid.NewGuid());
        _server.OnNumeric("GET", $"/cours/{CourseId}", course);
        var api = Get<ICourseApi>();

        Assert.True((await api.GetAsync(CourseId)).Ok);
        _server.Down = true;

        var offline = await api.GetAsync(CourseId);
        Assert.True(offline.Ok);
        Assert.Equal("MRUV", offline.Value!.Title);
        Assert.False(Get<IOfflineStatus>().Online);

        var missing = await api.GetAsync(Guid.NewGuid());
        Assert.False(missing.Ok);
        Assert.Contains("pas été téléchargé", missing.Error);
    }

    [Fact]
    public async Task Composition_hors_ligne_puis_envoi_au_retour_du_reseau()
    {
        await SignInAsync();
        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package());
        _server.On("GET", "/assessments/mine", new List<StudentAssessment> { Listed() });
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();

        Assert.Null(await status.MakeAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId));
        Assert.True(await status.IsAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId));
        await api.MineAsStudentAsync();

        // ── Coupure : composition sur l'appareil ──
        _server.Down = true;
        var attempt = await api.StartAttemptAsync(AssessmentId);
        Assert.True(attempt.Ok, attempt.Error);
        Assert.Equal(2, attempt.Value!.Questions.Count);
        Assert.NotNull(attempt.Value.Deadline);

        var photo = await api.UploadPhotoAsync(AssessmentId, attempt.Value.SubmissionId, OpenQuestion, Photo());
        Assert.True(photo.Ok);
        Assert.StartsWith("data:image/jpeg;base64,", await api.PhotoAsync(AssessmentId, attempt.Value.SubmissionId, photo.Value!.Id));

        // Reprise : la même tentative locale, avec sa photo
        var resumed = await api.StartAttemptAsync(AssessmentId);
        Assert.Equal(attempt.Value.SubmissionId, resumed.Value!.SubmissionId);
        Assert.Single(resumed.Value.Photos!);

        var answers = new List<AnswerDraft> { new() { QuestionId = ChoiceQuestion, SelectedOptions = [1] }, new() { QuestionId = OpenQuestion } };
        var submitted = await api.SubmitAsync(AssessmentId, attempt.Value.SubmissionId, answers);
        Assert.True(submitted.Value!.PendingSync);
        Assert.Equal(1, status.Pending);

        var list = await api.MineAsStudentAsync();
        Assert.Equal(1, list.Value![0].AttemptsUsed);
        Assert.Equal(attempt.Value.SubmissionId, list.Value[0].LastSubmissionId);

        // ── Retour du réseau : la copie part, avec sa photo et l'identifiant de l'appareil ──
        var serverId = Guid.NewGuid();
        _server.On("POST", $"/assessments/{AssessmentId}/offline-submissions", _ => (HttpStatusCode.Created, Review(serverId)));
        _server.On("GET", $"/assessments/{AssessmentId}/submissions/{serverId}", Review(serverId));
        _server.Down = false;
        await status.SyncNowAsync();

        Assert.Equal(0, status.Pending);
        var sent = _server.Requests.Single(r => r.Path.EndsWith("/offline-submissions"));
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal(attempt.Value.SubmissionId, json.RootElement.GetProperty("clientSubmissionId").GetGuid());
        Assert.Equal(1, json.RootElement.GetProperty("photos").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("answers").GetArrayLength());

        // L'adresse locale de la copie mène à la copie du serveur
        var review = await api.GetSubmissionAsync(AssessmentId, attempt.Value.SubmissionId);
        Assert.Equal(serverId, review.Value!.SubmissionId);
        Assert.False(review.Value.PendingSync);
    }

    [Fact]
    public async Task Une_coupure_pendant_l_envoi_ne_perd_rien()
    {
        await SignInAsync();
        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package());
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();
        await status.MakeAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId);
        _server.Down = true;
        var attempt = await api.StartAttemptAsync(AssessmentId);
        await api.SubmitAsync(AssessmentId, attempt.Value!.SubmissionId, []);

        await status.SyncNowAsync();           // toujours coupé
        Assert.Equal(1, status.Pending);

        // Le serveur a enregistré la copie mais la réponse s'est perdue : le renvoi est idempotent côté serveur
        var serverId = Guid.NewGuid();
        var calls = 0;
        _server.On("POST", $"/assessments/{AssessmentId}/offline-submissions", _ =>
        {
            calls++;
            return (HttpStatusCode.OK, Review(serverId));
        });
        _server.Down = false;
        await status.SyncNowAsync();
        Assert.Equal(0, status.Pending);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Un_refus_du_serveur_est_signale_a_l_eleve()
    {
        await SignInAsync();
        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package());
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();
        await status.MakeAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId);
        _server.Down = true;
        var attempt = await api.StartAttemptAsync(AssessmentId);
        await api.SubmitAsync(AssessmentId, attempt.Value!.SubmissionId, []);

        _server.On("POST", $"/assessments/{AssessmentId}/offline-submissions",
            _ => (HttpStatusCode.Conflict, new { message = "Vous avez utilisé toutes vos tentatives." }));
        _server.Down = false;
        await status.SyncNowAsync();

        Assert.Equal(0, status.Pending);
        Assert.Contains(status.Problems, p => p.Contains("toutes vos tentatives"));
    }

    [Fact]
    public async Task Une_erreur_passagere_du_serveur_est_retentee()
    {
        await SignInAsync();
        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package());
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();
        await status.MakeAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId);
        _server.Down = true;
        var attempt = await api.StartAttemptAsync(AssessmentId);
        await api.SubmitAsync(AssessmentId, attempt.Value!.SubmissionId, []);

        _server.On("POST", $"/assessments/{AssessmentId}/offline-submissions", _ => (HttpStatusCode.InternalServerError, new { message = "panne" }));
        _server.Down = false;
        await status.SyncNowAsync();
        Assert.Equal(1, status.Pending);          // gardée, pas rejetée
        Assert.Empty(status.Problems);

        _server.On("POST", $"/assessments/{AssessmentId}/offline-submissions", _ => (HttpStatusCode.Created, Review(Guid.NewGuid())));
        await status.SyncNowAsync();
        Assert.Equal(0, status.Pending);
    }

    [Fact]
    public async Task Les_tentatives_epuisees_sur_l_appareil_bloquent_une_nouvelle_composition()
    {
        await SignInAsync();
        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package(attemptsLeft: 1));
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();
        await status.MakeAvailableOfflineAsync(OfflineItemKind.Assessment, AssessmentId);
        _server.Down = true;
        var first = await api.StartAttemptAsync(AssessmentId);
        await api.SubmitAsync(AssessmentId, first.Value!.SubmissionId, []);

        var second = await api.StartAttemptAsync(AssessmentId);
        Assert.False(second.Ok);
        Assert.Contains("toutes tes tentatives", second.Error);
    }

    [Fact]
    public async Task Une_copie_en_ligne_rendue_pendant_une_coupure_est_mise_en_attente()
    {
        await SignInAsync();
        var submissionId = Guid.NewGuid();
        var status = Get<IOfflineStatus>();
        var api = Get<IAssessmentApi>();

        _server.Down = true;
        var photo = await api.UploadPhotoAsync(AssessmentId, submissionId, OpenQuestion, Photo());
        Assert.True(photo.Ok);
        var pending = await api.SubmitAsync(AssessmentId, submissionId, [new() { QuestionId = OpenQuestion, Text = "x = 25 m" }]);
        Assert.True(pending.Value!.PendingSync);
        Assert.Equal(2, status.Pending);

        _server.On("POST", $"/assessments/{AssessmentId}/attempts/{submissionId}/questions/{OpenQuestion}/photos",
            _ => (HttpStatusCode.OK, new AnswerPhoto(Guid.NewGuid(), OpenQuestion, 1, "x = 25 m")));
        _server.On("POST", $"/assessments/{AssessmentId}/attempts/{submissionId}/submit", _ => (HttpStatusCode.OK, Review(submissionId)));
        _server.Down = false;
        await status.SyncNowAsync();

        Assert.Equal(0, status.Pending);
        var order = _server.Requests.Where(r => r.Method == "POST").Select(r => r.Path).ToList();
        Assert.EndsWith("/photos", order[0]);       // la photo d'abord
        Assert.EndsWith("/submit", order[1]);       // puis la copie
    }

    [Fact]
    public async Task Rien_n_est_envoye_avec_un_jeton_expire()
    {
        await SignInAsync(expires: DateTime.UtcNow.AddMinutes(-5));
        var session = Get<SessionState>();
        Assert.True(session.IsAuthenticated);    // la session reste ouverte hors connexion
        Assert.True(session.TokenExpired);

        _server.On("GET", $"/assessments/{AssessmentId}/offline-package", Package());
        await Get<OfflineDatabase>().SaveAsync("package", AssessmentId.ToString(), Package());
        var api = Get<IAssessmentApi>();
        _server.Down = true;
        var attempt = await api.StartAttemptAsync(AssessmentId);
        await api.SubmitAsync(AssessmentId, attempt.Value!.SubmissionId, []);
        _server.Down = false;
        await Get<IOfflineStatus>().SyncNowAsync();

        Assert.Equal(1, Get<IOfflineStatus>().Pending);
        Assert.DoesNotContain(_server.Requests, r => r.Path.EndsWith("/offline-submissions"));
    }

    [Fact]
    public async Task Le_reseau_est_detecte_a_son_retour()
    {
        await SignInAsync();
        var monitor = _root.GetRequiredService<NetworkMonitor>();
        _server.Down = true;
        await Get<ICourseApi>().SharedWithMeAsync();
        Assert.False(monitor.Online);

        _server.Down = false;   // la sonde (toutes les 100 ms ici) rétablit l'état
        for (var i = 0; i < 50 && !monitor.Online; i++) await Task.Delay(50);
        Assert.True(monitor.Online);
    }

    [Fact]
    public async Task Chaque_utilisateur_a_sa_propre_base()
    {
        await SignInAsync();
        var path = Get<OfflineDatabase>().Path;
        Assert.Contains(StudentId.ToString("N"), path);
        await Get<OfflineDatabase>().SaveAsync("x", "y", 1);
        Assert.True(File.Exists(path));
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
