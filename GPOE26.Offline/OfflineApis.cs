using GPOE26.UI.Models;
using GPOE26.UI.Services;

namespace GPOE26.Offline;

// Versions hors connexion des API de lecture : la dernière réponse reçue est conservée sur l'appareil.

public class OfflineCourseApi(CourseApi inner, OfflineDatabase db, NetworkMonitor net) : ICourseApi
{
    public Task<ApiResult<List<CourseSummary>>> MineAsync() => db.CachedAsync(net, "courses-mine", "", inner.MineAsync);
    public Task<ApiResult<List<CourseSummary>>> SharedWithMeAsync() => db.CachedAsync(net, "courses-shared", "", inner.SharedWithMeAsync);
    public Task<ApiResult<CourseDetail>> GetAsync(Guid id) => db.CachedAsync(net, "course", id.ToString(), () => inner.GetAsync(id),
        "Hors connexion : ce cours n'a pas été téléchargé. Ouvre-le une fois connecté, ou utilise « Disponible hors ligne ».");
    public Task<ApiResult<CourseSummary>> ShareAsync(Guid id, bool publish, List<Guid> classroomIds) =>
        net.TrackAsync(inner.ShareAsync(id, publish, classroomIds));
}

public class OfflineProgressApi(ProgressApi inner, OfflineDatabase db, NetworkMonitor net) : IProgressApi
{
    public Task<ApiResult<StudentProgress>> MineAsync() => db.CachedAsync(net, "progress-me", "", inner.MineAsync);
    public Task<ApiResult<StudentProgress>> ChildAsync(Guid studentId) => db.CachedAsync(net, "progress-child", studentId.ToString(), () => inner.ChildAsync(studentId));
    public Task<ApiResult<ClassroomProgress>> ClassroomAsync(Guid classroomId) => db.CachedAsync(net, "progress-class", classroomId.ToString(), () => inner.ClassroomAsync(classroomId));
}

public class OfflineClassroomApi(ClassroomApi inner, OfflineDatabase db, NetworkMonitor net) : IClassroomApi
{
    public Task<ApiResult<List<Classroom>>> MineAsync() => db.CachedAsync(net, "classrooms", "", inner.MineAsync);
    public Task<ApiResult<Classroom>> GetAsync(Guid id) => db.CachedAsync(net, "classroom", id.ToString(), () => inner.GetAsync(id));
    public Task<ApiResult<Classroom>> CreateAsync(CreateClassroomRequest request) => net.TrackAsync(inner.CreateAsync(request));
    public Task<ApiResult<Classroom>> JoinAsync(string code) => net.TrackAsync(inner.JoinAsync(code));
    public Task<ApiResult<List<ClassroomMember>>> MembersAsync(Guid id) => db.CachedAsync(net, "members", id.ToString(), () => inner.MembersAsync(id));
    public Task<ApiResult<bool>> RemoveMemberAsync(Guid classroomId, Guid studentId) => net.TrackAsync(inner.RemoveMemberAsync(classroomId, studentId));
    public Task<ApiResult<bool>> RegenerateCodeAsync(Guid id) => net.TrackAsync(inner.RegenerateCodeAsync(id));
}

public class OfflineTutorApi(TutorApi inner, OfflineDatabase db, NetworkMonitor net) : ITutorApi
{
    private const string NeedsNetwork = "Le tuteur a besoin d'une connexion internet. Tes discussions enregistrées restent lisibles.";

    public Task<ApiResult<List<TutorConversationSummary>>> ConversationsAsync() => db.CachedAsync(net, "tutor-list", "", inner.ConversationsAsync);
    public Task<ApiResult<TutorConversation>> ConversationAsync(Guid courseId) =>
        db.CachedAsync(net, "tutor-conversation", courseId.ToString(), () => inner.ConversationAsync(courseId), NeedsNetwork);
    public Task<ApiResult<List<PinnedExplanation>>> PinnedAsync() => db.CachedAsync(net, "tutor-pinned", "", inner.PinnedAsync);

    public async Task<ApiResult<TutorExchange>> AskAsync(Guid courseId, string? message, Guid? imageId)
    {
        var result = await net.TrackAsync(inner.AskAsync(courseId, message, imageId));
        return result.NetworkError ? result with { Error = NeedsNetwork } : result;
    }

    public async Task<ApiResult<UploadedImage>> UploadImageAsync(ImageUpload image)
    {
        var result = await net.TrackAsync(inner.UploadImageAsync(image));
        return result.NetworkError ? result with { Error = NeedsNetwork } : result;
    }

    public Task<string?> ImageAsync(Guid imageId) => inner.ImageAsync(imageId);
    public Task<ApiResult<TutorMessage>> PinAsync(Guid messageId, bool pinned) => net.TrackAsync(inner.PinAsync(messageId, pinned));
    public Task<ApiResult<bool>> ClearAsync(Guid courseId) => net.TrackAsync(inner.ClearAsync(courseId));
}
