using System.Net;
using System.Text.Json;
using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Offline;

/// <summary>
/// Évaluations avec composition hors connexion :
/// - une évaluation téléchargée (« paquet ») se compose sans réseau ; la copie et ses photos restent sur
///   l'appareil puis partent dans la file d'envoi ;
/// - une copie commencée en ligne qui ne peut pas être rendue (coupure) est mise dans la file, avec ses photos,
///   au lieu d'être perdue ;
/// - les copies déjà consultées restent lisibles hors connexion.
/// </summary>
public class OfflineAssessmentApi(AssessmentApi inner, OfflineDatabase db, NetworkMonitor net, SyncService sync, SessionState session)
    : IAssessmentApi
{
    private const string NotDownloaded =
        "Hors connexion : cette évaluation n'a pas été téléchargée. Une fois connecté, utilise « Composer hors ligne » dans « Mes évaluations ».";

    // ─── Enseignant (en ligne uniquement) ─────────────────────────────────────

    public Task<ApiResult<List<Assessment>>> MineAsTeacherAsync() => db.CachedAsync(net, "assessments-teacher", "", inner.MineAsTeacherAsync);
    public Task<ApiResult<Assessment>> GetAsync(Guid id) => db.CachedAsync(net, "assessment", id.ToString(), () => inner.GetAsync(id));
    public Task<ApiResult<Assessment>> CreateAsync(AssessmentDraft draft) => net.TrackAsync(inner.CreateAsync(draft));
    public Task<ApiResult<Assessment>> UpdateAsync(Guid id, AssessmentDraft draft) => net.TrackAsync(inner.UpdateAsync(id, draft));
    public Task<ApiResult<Assessment>> PublishAsync(Guid id) => net.TrackAsync(inner.PublishAsync(id));
    public Task<ApiResult<Assessment>> CloseAsync(Guid id) => net.TrackAsync(inner.CloseAsync(id));
    public Task<ApiResult<bool>> DeleteAsync(Guid id) => net.TrackAsync(inner.DeleteAsync(id));
    public Task<ApiResult<List<SubmissionRow>>> SubmissionsAsync(Guid id) => db.CachedAsync(net, "submissions", id.ToString(), () => inner.SubmissionsAsync(id));
    public Task<ApiResult<SubmissionReview>> GradeAsync(Guid id, Guid submissionId, Guid questionId, decimal points, string? feedback) =>
        net.TrackAsync(inner.GradeAsync(id, submissionId, questionId, points, feedback));
    public Task<ApiResult<AiSuggestion>> AiSuggestionAsync(Guid id, Guid submissionId, Guid questionId) =>
        net.TrackAsync(inner.AiSuggestionAsync(id, submissionId, questionId));

    // ─── Élève ────────────────────────────────────────────────────────────────

    public async Task<ApiResult<List<StudentAssessment>>> MineAsStudentAsync()
    {
        var result = await db.CachedAsync(net, "assessments-mine", "", inner.MineAsStudentAsync);
        if (result.Value is null) return result;

        // Les copies encore sur l'appareil comptent déjà comme rendues (ou en cours)
        var local = (await db.AllAttemptsAsync()).Where(a => a.State is AttemptStates.Composing or AttemptStates.Pending).ToList();
        var merged = result.Value.Select(a =>
        {
            var mine = local.Where(l => l.AssessmentId == a.Id).ToList();
            var pendingOffline = mine.Count(l => l.State == AttemptStates.Pending && l.Mode == "offline");
            var pendingOnline = mine.Count(l => l.State == AttemptStates.Pending && l.Mode == "online");
            if (mine.Count == 0) return a;
            return a with
            {
                AttemptsUsed = a.AttemptsUsed + pendingOffline + pendingOnline,
                HasAttemptInProgress = mine.Any(l => l.State == AttemptStates.Composing) || (a.HasAttemptInProgress && pendingOnline == 0),
                LastSubmissionId = mine.Where(l => l.State == AttemptStates.Pending).MaxBy(l => l.SubmittedAt)?.Id ?? a.LastSubmissionId
            };
        }).ToList();
        return result with { Value = merged };
    }

    public async Task<ApiResult<Attempt>> StartAttemptAsync(Guid id)
    {
        // Une composition hors ligne commencée se poursuit sur l'appareil, même si le réseau est revenu
        var composing = (await db.AttemptsForAsync(id)).FirstOrDefault(a => a.Mode == "offline" && a.State == AttemptStates.Composing);
        if (composing is not null && await db.LoadAsync<OfflinePackage>("package", id.ToString()) is { } current)
            return Ok(await ToAttemptAsync(current, composing));

        var online = await net.TrackAsync(inner.StartAttemptAsync(id));
        if (!online.NetworkError) return online;

        var package = await db.LoadAsync<OfflinePackage>("package", id.ToString());
        if (package is null) return online with { Error = NotDownloaded };

        var now = DateTime.UtcNow;
        var localDone = (await db.AttemptsForAsync(id)).Count(a => a.Mode == "offline" && a.State is AttemptStates.Pending or AttemptStates.Sent);
        if (package.OpensAt > now) return Error<Attempt>($"Cette évaluation ouvre le {AppTime.ToLocal(package.OpensAt.Value):dd/MM/yyyy à HH:mm}.");
        if (package.DueAt <= now) return Error<Attempt>("La date limite est passée.");
        if (package.AttemptsLeft - localDone <= 0) return Error<Attempt>("Tu as utilisé toutes tes tentatives.");

        DateTime? deadline = package.DurationMinutes is { } minutes ? now.AddMinutes(minutes) : null;
        if (package.DueAt is not null && (deadline is null || package.DueAt < deadline)) deadline = package.DueAt;

        // Numéro de tentative : copies déjà reçues par le serveur + copies de l'appareil pas encore envoyées
        var usedOnServer = (await db.LoadAsync<List<StudentAssessment>>("assessments-mine", ""))?.FirstOrDefault(a => a.Id == id)?.AttemptsUsed ?? 0;
        var waiting = (await db.AttemptsForAsync(id)).Count(a => a.Mode == "offline" && a.State == AttemptStates.Pending);
        var attempt = new LocalAttempt(Guid.NewGuid(), id, "offline", AttemptStates.Composing, now, deadline, null, null, null,
            package.Title, usedOnServer + waiting + 1, package.MaxScore, package.ScaleTo, null);
        await db.SaveAttemptAsync(attempt);
        return Ok(await ToAttemptAsync(package, attempt));
    }

    public async Task<ApiResult<SubmissionReview>> SubmitAsync(Guid id, Guid submissionId, List<AnswerDraft> answers)
    {
        var local = await db.AttemptAsync(submissionId);
        if (local is { Mode: "offline", State: AttemptStates.Composing })
        {
            local = local with { State = AttemptStates.Pending, SubmittedAt = DateTime.UtcNow, AnswersJson = Serialize(answers) };
            await db.SaveAttemptAsync(local);
            await db.EnqueueAsync(OutboxKinds.OfflineSubmission, local.Id);
            await sync.EnqueuedAsync();
            return Ok(Pending(local));
        }

        // Copie en ligne : si des photos attendent encore d'être envoyées, la remise passe après elles
        if (!await db.HasPendingForAsync(submissionId))
        {
            var result = await net.TrackAsync(inner.SubmitAsync(id, submissionId, answers));
            if (!result.NetworkError) return result;
        }

        var meta = (await db.LoadAsync<List<StudentAssessment>>("assessments-mine", ""))?.FirstOrDefault(a => a.Id == id);
        var queued = new LocalAttempt(submissionId, id, "online", AttemptStates.Pending, local?.StartedAt ?? DateTime.UtcNow, null,
            DateTime.UtcNow, Serialize(answers), submissionId, meta?.Title ?? "Évaluation", (meta?.AttemptsUsed ?? 0) + 1,
            meta?.MaxScore ?? 0, meta?.ScaleTo ?? 20, null);
        await db.SaveAttemptAsync(queued);
        await db.EnqueueAsync(OutboxKinds.OnlineSubmit, submissionId);
        await sync.EnqueuedAsync();
        return Ok(Pending(queued));
    }

    public async Task<ApiResult<SubmissionReview>> GetSubmissionAsync(Guid id, Guid submissionId)
    {
        var local = await db.AttemptAsync(submissionId);
        if (local is { State: AttemptStates.Pending or AttemptStates.Rejected })
            return Ok(Pending(local));
        var serverId = local?.ServerId ?? submissionId;
        return await db.CachedAsync(net, "submission", serverId.ToString(), () => inner.GetSubmissionAsync(id, serverId));
    }

    public async Task<ApiResult<AnswerPhoto>> UploadPhotoAsync(Guid id, Guid submissionId, Guid questionId, ImageUpload photo)
    {
        var local = await db.AttemptAsync(submissionId);
        if (local is { Mode: "offline", State: AttemptStates.Composing })
            return Ok(await StorePhotoAsync(id, submissionId, questionId, photo));

        var result = await net.TrackAsync(inner.UploadPhotoAsync(id, submissionId, questionId, photo));
        if (!result.NetworkError) return result;

        // Coupure pendant une composition en ligne : la photo attend le retour du réseau
        var stored = await StorePhotoAsync(id, submissionId, questionId, photo);
        await db.EnqueueAsync(OutboxKinds.OnlinePhoto, stored.Id);
        await sync.EnqueuedAsync();
        return Ok(stored);
    }

    public async Task<ApiResult<bool>> DeletePhotoAsync(Guid id, Guid submissionId, Guid photoId)
    {
        if (await db.PhotoAsync(photoId) is { } local)
        {
            await db.DeletePhotoAsync(photoId);
            await db.RenumberPhotosAsync(local.AttemptId, local.QuestionId);
            await sync.EnqueuedAsync();
            return Ok(true);
        }
        return await net.TrackAsync(inner.DeletePhotoAsync(id, submissionId, photoId));
    }

    public async Task<string?> PhotoAsync(Guid id, Guid submissionId, Guid photoId)
    {
        if (await db.PhotoAsync(photoId) is { } local)
            return $"data:{local.ContentType};base64,{Convert.ToBase64String(local.Data)}";
        return await inner.PhotoAsync(id, await ServerIdAsync(submissionId), photoId);
    }

    public async Task<ApiResult<AiFeedback>> AiFeedbackAsync(Guid id, Guid submissionId, Guid questionId) =>
        await net.TrackAsync(inner.AiFeedbackAsync(id, await ServerIdAsync(submissionId), questionId));

    // ─── Outils ───────────────────────────────────────────────────────────────

    private async Task<Guid> ServerIdAsync(Guid submissionId) => (await db.AttemptAsync(submissionId))?.ServerId ?? submissionId;

    private async Task<AnswerPhoto> StorePhotoAsync(Guid assessmentId, Guid attemptId, Guid questionId, ImageUpload photo)
    {
        var page = (await db.PhotosOfAsync(attemptId)).Count(p => p.QuestionId == questionId) + 1;
        if (page > 4) throw new InvalidOperationException("4 photos au maximum par question.");
        var stored = new LocalPhoto(Guid.NewGuid(), attemptId, assessmentId, questionId, page, photo.ContentType, photo.Data);
        await db.AddPhotoAsync(stored);
        // Transcription null : l'IA lira la copie une fois envoyée
        return new AnswerPhoto(stored.Id, questionId, page, null);
    }

    private async Task<Attempt> ToAttemptAsync(OfflinePackage p, LocalAttempt a)
    {
        var photos = (await db.PhotosOfAsync(a.Id)).Select(x => new AnswerPhoto(x.Id, x.QuestionId, x.Page, null)).ToList();
        return new Attempt(a.Id, p.AssessmentId, p.Title, a.AttemptNumber, a.StartedAt, a.Deadline, p.Questions, photos);
    }

    private SubmissionReview Pending(LocalAttempt a) =>
        new(a.Id, a.AssessmentId, a.Title, session.Profile?.Id ?? Guid.Empty, a.AttemptNumber, SubmissionStatus.Submitted,
            a.StartedAt, a.SubmittedAt, false, false, null, a.MaxScore, null, a.ScaleTo, null, Offline: true)
        { PendingSync = a.State == AttemptStates.Pending };

    private static string Serialize(List<AnswerDraft> answers) => JsonSerializer.Serialize(answers, OfflineDatabase.Json);

    private static ApiResult<T> Ok<T>(T value) => new(value, null, HttpStatusCode.OK);
    private static ApiResult<T> Error<T>(string message) => new(default, message, HttpStatusCode.Conflict);
}
