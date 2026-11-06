using System.Net;
using System.Text.Json;
using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Offline;

/// <summary>
/// Vide la file d'envoi dans l'ordre (les photos d'une copie partent avant la copie) dès que le réseau revient,
/// après une connexion, ou à la demande. Une coupure arrête la synchronisation sans rien perdre ;
/// un refus du serveur (tentatives épuisées…) est signalé à l'élève et retiré de la file.
/// </summary>
public class SyncService(AssessmentApi api, OfflineDatabase db, NetworkMonitor net, SessionState session) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _problems = new();
    private bool _subscribed;

    public bool Syncing { get; private set; }
    public int Pending { get; private set; }
    public DateTime? LastSync { get; private set; }
    public IReadOnlyList<string> Problems => _problems;

    public event Action? Changed;

    /// <summary>Démarre l'écoute du réseau et de la session (une fois par portée)</summary>
    public void Start()
    {
        if (_subscribed) return;
        _subscribed = true;
        net.Changed += OnNetworkChanged;
        session.Changed += OnSessionChanged;
        _ = KickAsync();
    }

    private void OnNetworkChanged(bool online)
    {
        if (online) _ = KickAsync();
        Changed?.Invoke();
    }

    private void OnSessionChanged() => _ = KickAsync();

    /// <summary>Un élément vient d'être mis dans la file</summary>
    public async Task EnqueuedAsync()
    {
        await RefreshAsync();
        _ = KickAsync();
    }

    public async Task RefreshAsync()
    {
        if (session.Profile is null) { Pending = 0; Changed?.Invoke(); return; }
        Pending = (await db.OutboxAsync()).Count;
        Changed?.Invoke();
    }

    private async Task KickAsync()
    {
        try { await SyncAsync(); }
        catch (Exception) { /* nouvelle tentative au prochain retour du réseau */ }
    }

    public async Task SyncAsync()
    {
        if (session.Profile is null || !session.IsAuthenticated) return;
        await RefreshAsync();
        if (Pending == 0 || session.TokenExpired) return;
        // Demande explicite ou retour supposé du réseau : on vérifie d'abord que le service répond
        if (!net.Online && !await net.ProbeNowAsync()) return;
        if (!await _gate.WaitAsync(0)) return;
        Syncing = true;
        Changed?.Invoke();
        try
        {
            foreach (var entry in await db.OutboxAsync())
            {
                await db.CountTryAsync(entry.Seq);
                var outcome = await SendAsync(entry);
                if (outcome == Outcome.Stop) break;
                await db.DequeueAsync(entry.Seq);
            }
            LastSync = DateTime.UtcNow;
            if (Pending == 0) LastError = null;
        }
        finally
        {
            Syncing = false;
            _gate.Release();
            await RefreshAsync();
        }
    }

    private enum Outcome { Done, Stop }

    private async Task<Outcome> SendAsync(OutboxEntry entry)
    {
        switch (entry.Kind)
        {
            case OutboxKinds.OfflineSubmission:
            {
                var attempt = await db.AttemptAsync(entry.Ref);
                if (attempt is null) return Outcome.Done;
                var photos = await db.PhotosOfAsync(attempt.Id);
                var request = new OfflineSubmissionRequest(attempt.Id, attempt.StartedAt, attempt.SubmittedAt ?? DateTime.UtcNow,
                    Answers(attempt),
                    photos.Select(p => new OfflinePhotoInput(p.QuestionId, p.Page, p.ContentType, Convert.ToBase64String(p.Data))).ToList());
                var result = await api.SubmitOfflineAsync(attempt.AssessmentId, request);
                if (Interrupted(result)) return Outcome.Stop;
                if (result.Ok)
                {
                    await db.SaveAttemptAsync(attempt with { State = AttemptStates.Sent, ServerId = result.Value!.SubmissionId, AnswersJson = null });
                    await db.SaveAsync("submission", result.Value.SubmissionId.ToString(), result.Value);
                    foreach (var p in photos) await db.DeletePhotoAsync(p.Id);
                }
                else Reject(attempt, result.Error);
                return Outcome.Done;
            }

            case OutboxKinds.OnlinePhoto:
            {
                var photo = await db.PhotoAsync(entry.Ref);
                if (photo is null) return Outcome.Done;
                var result = await api.UploadPhotoAsync(photo.AssessmentId, photo.AttemptId, photo.QuestionId,
                    new ImageUpload($"page-{photo.Page}.jpg", photo.ContentType, photo.Data));
                if (Interrupted(result)) return Outcome.Stop;
                if (!result.Ok) _problems.Add($"Une photo de copie n'a pas pu être envoyée : {result.Error}");
                await db.DeletePhotoAsync(photo.Id);
                return Outcome.Done;
            }

            case OutboxKinds.OnlineSubmit:
            {
                var attempt = await db.AttemptAsync(entry.Ref);
                if (attempt is null) return Outcome.Done;
                var result = await api.SubmitAsync(attempt.AssessmentId, attempt.Id, Answers(attempt));
                if (Interrupted(result)) return Outcome.Stop;
                // 409 « déjà rendue » : un premier envoi a abouti avant la coupure
                if (result.Ok || result.Status == HttpStatusCode.Conflict)
                    await db.SaveAttemptAsync(attempt with { State = AttemptStates.Sent, AnswersJson = null });
                else Reject(attempt, result.Error);
                return Outcome.Done;
            }

            default:
                return Outcome.Done;
        }
    }

    /// <summary>
    /// Coupure, session expirée ou erreur passagère du serveur (5xx) : on garde l'élément et on réessaiera.
    /// Seul un refus explicite (4xx : tentatives épuisées, évaluation fermée…) retire l'élément de la file.
    /// </summary>
    private bool Interrupted<T>(ApiResult<T> result)
    {
        net.Report(result.NetworkError);
        if (!result.Ok && !result.NetworkError && (int)result.Status >= 500)
            LastError = result.Error;
        return result.NetworkError || result.Status == HttpStatusCode.Unauthorized || (int)result.Status >= 500;
    }

    /// <summary>Dernière erreur passagère du serveur (l'envoi sera retenté)</summary>
    public string? LastError { get; private set; }

    private void Reject(LocalAttempt attempt, string? error)
    {
        _problems.Add($"« {attempt.Title} » : {error}");
        _ = db.SaveAttemptAsync(attempt with { State = AttemptStates.Rejected, Error = error });
    }

    private static List<AnswerDraft> Answers(LocalAttempt a) =>
        a.AnswersJson is null ? new() : JsonSerializer.Deserialize<List<AnswerDraft>>(a.AnswersJson, OfflineDatabase.Json) ?? new();

    public void Dispose()
    {
        net.Changed -= OnNetworkChanged;
        session.Changed -= OnSessionChanged;
    }
}
