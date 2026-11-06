using GPOE26.UI.Services;

namespace GPOE26.Offline;

/// <summary>État hors connexion pour les pages (bandeau, boutons « Disponible hors ligne »).</summary>
public class OfflineStatusService : IOfflineStatus, IDisposable
{
    private readonly SyncService _sync;
    private readonly NetworkMonitor _net;
    private readonly OfflineDatabase _db;
    private readonly AssessmentApi _assessments;
    private readonly ICourseApi _courses;

    public OfflineStatusService(SyncService sync, NetworkMonitor net, OfflineDatabase db, AssessmentApi assessments, ICourseApi courses)
    {
        (_sync, _net, _db, _assessments, _courses) = (sync, net, db, assessments, courses);
        _sync.Changed += OnChanged;
        _sync.Start();
    }

    public bool Supported => true;
    public bool Online => _net.Online;
    public int Pending => _sync.Pending;
    public IReadOnlyList<string> Problems => _sync.Problems;
    public bool Syncing => _sync.Syncing;
    public DateTime? LastSync => _sync.LastSync;

    public event Action? Changed;

    public Task<bool> IsAvailableOfflineAsync(OfflineItemKind kind, Guid id) =>
        _db.HasAsync(kind == OfflineItemKind.Course ? "course" : "package", id.ToString());

    public async Task<string?> MakeAvailableOfflineAsync(OfflineItemKind kind, Guid id)
    {
        if (kind == OfflineItemKind.Course)
        {
            // La version hors ligne de ICourseApi met le cours en cache
            var course = await _courses.GetAsync(id);
            return course.Ok && !course.NetworkError ? null : course.Error ?? "Connexion nécessaire pour télécharger.";
        }

        var package = await _net.TrackAsync(_assessments.OfflinePackageAsync(id));
        if (!package.Ok) return package.Error;
        await _db.SaveAsync("package", id.ToString(), package.Value!);
        return null;
    }

    public Task SyncNowAsync() => _sync.SyncAsync();

    private void OnChanged() => Changed?.Invoke();

    public void Dispose() => _sync.Changed -= OnChanged;
}
