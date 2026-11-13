using GPOE26.UI.Models;

namespace GPOE26.UI.Services;

/// <summary>
/// Gestion d'un espace (établissement ou enseignant indépendant) par son responsable et ses gestionnaires,
/// et activation des codes personnels (docs/PLAN-ETABLISSEMENT.md).
/// </summary>
public interface ISpaceApi
{
    Task<ApiResult<List<MySpace>>> MineAsync();
    Task<ApiResult<SpaceOverview>> OverviewAsync(Guid id);

    Task<ApiResult<List<SchoolYear>>> YearsAsync(Guid id);
    Task<ApiResult<SchoolYear>> CreateYearAsync(Guid id, string label);
    Task<ApiResult<bool>> SetCurrentYearAsync(Guid id, Guid yearId);

    Task<ApiResult<List<SpaceMember>>> MembersAsync(Guid id);
    Task<ApiResult<bool>> AddTeacherAsync(Guid id, string email);
    Task<ApiResult<bool>> RemoveTeacherAsync(Guid id, Guid userId);
    Task<ApiResult<bool>> AddManagerAsync(Guid id, string email);
    Task<ApiResult<bool>> RemoveManagerAsync(Guid id, Guid userId);

    Task<ApiResult<List<SpaceClass>>> ClassesAsync(Guid id, Guid? yearId = null);
    Task<ApiResult<SpaceClass>> CreateClassAsync(Guid id, SpaceClassRequest request);
    Task<ApiResult<bool>> UpdateClassAsync(Guid id, Guid classId, SpaceClassRequest request);
    Task<ApiResult<bool>> ArchiveClassAsync(Guid id, Guid classId);
    Task<ApiResult<List<ClassTeacher>>> AssignTeacherAsync(Guid id, Guid classId, AssignTeacherRequest request);
    Task<ApiResult<bool>> UnassignTeacherAsync(Guid id, Guid classId, Guid teacherId, string? subject);

    Task<ApiResult<List<SpaceStudent>>> StudentsAsync(Guid id, Guid? classId = null, StudentStatus? status = null);
    Task<ApiResult<SpaceStudent>> AddStudentAsync(Guid id, StudentRequest request);
    Task<ApiResult<SpaceStudent>> UpdateStudentAsync(Guid id, Guid studentId, StudentRequest request);
    Task<ApiResult<SpaceStudent>> SetStudentStatusAsync(Guid id, Guid studentId, StudentStatus status);
    Task<ApiResult<bool>> DeleteStudentAsync(Guid id, Guid studentId);
    Task<ApiResult<ImportResult>> ImportStudentsAsync(Guid id, string csv, bool commit);
    /// <summary>Aperçu d'un fichier Excel (.xlsx), Word (.docx) ou CSV ; le résultat contient le CSV à renvoyer pour enregistrer.</summary>
    Task<ApiResult<ImportResult>> ImportStudentsFileAsync(Guid id, ImageUpload file);

    Task<ApiResult<List<AccessCodeInfo>>> CodesAsync(Guid id, Guid? classId = null, AccessCodeKind? kind = null);
    Task<ApiResult<SpaceStudent>> IssueStudentCodeAsync(Guid id, Guid studentId);
    Task<ApiResult<List<AccessCodeInfo>>> IssueClassCodesAsync(Guid id, Guid classId);
    Task<ApiResult<List<AccessCodeInfo>>> IssueBlankCodesAsync(Guid id, Guid classId, int count);
    Task<ApiResult<AccessCodeInfo>> IssueTeacherCodeAsync(Guid id);
    Task<ApiResult<bool>> RevokeCodeAsync(Guid id, Guid codeId);

    /// <summary>Sans compte : à qui est ce code ?</summary>
    Task<ApiResult<CodeInfo>> CheckCodeAsync(string code);
    /// <summary>Sans compte : crée le compte avec le code.</summary>
    Task<ApiResult<AuthResponse>> ActivateCodeAsync(ActivateCodeRequest request);
    /// <summary>Compte connecté : le rattache à l'espace du code.</summary>
    Task<ApiResult<RedeemCodeResult>> RedeemCodeAsync(string code);
}

public class SpaceApi(IHttpClientFactory f, SessionState s) : ApiBase(f, s, ApiClients.User), ISpaceApi
{
    private static string Q(params (string Name, object? Value)[] args)
    {
        var parts = args.Where(a => a.Value is not null).Select(a => $"{a.Name}={Uri.EscapeDataString(a.Value!.ToString()!)}").ToList();
        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }

    public Task<ApiResult<List<MySpace>>> MineAsync() => GetAsync<List<MySpace>>("/organizations/mine");
    public Task<ApiResult<SpaceOverview>> OverviewAsync(Guid id) => GetAsync<SpaceOverview>($"/organizations/{id}");

    public Task<ApiResult<List<SchoolYear>>> YearsAsync(Guid id) => GetAsync<List<SchoolYear>>($"/organizations/{id}/years");
    public Task<ApiResult<SchoolYear>> CreateYearAsync(Guid id, string label) => PostAsync<SchoolYear>($"/organizations/{id}/years", new { label });
    public Task<ApiResult<bool>> SetCurrentYearAsync(Guid id, Guid yearId) => PostAsync<bool>($"/organizations/{id}/years/{yearId}/current");

    public Task<ApiResult<List<SpaceMember>>> MembersAsync(Guid id) => GetAsync<List<SpaceMember>>($"/organizations/{id}/members");
    public Task<ApiResult<bool>> AddTeacherAsync(Guid id, string email) => PostAsync<bool>($"/organizations/{id}/teachers", new { email });
    public Task<ApiResult<bool>> RemoveTeacherAsync(Guid id, Guid userId) => DeleteAsync<bool>($"/organizations/{id}/teachers/{userId}");
    public Task<ApiResult<bool>> AddManagerAsync(Guid id, string email) => PostAsync<bool>($"/organizations/{id}/managers", new { email });
    public Task<ApiResult<bool>> RemoveManagerAsync(Guid id, Guid userId) => DeleteAsync<bool>($"/organizations/{id}/managers/{userId}");

    public Task<ApiResult<List<SpaceClass>>> ClassesAsync(Guid id, Guid? yearId = null) =>
        GetAsync<List<SpaceClass>>($"/organizations/{id}/classes{Q(("yearId", yearId))}");
    public Task<ApiResult<SpaceClass>> CreateClassAsync(Guid id, SpaceClassRequest request) => PostAsync<SpaceClass>($"/organizations/{id}/classes", request);
    public Task<ApiResult<bool>> UpdateClassAsync(Guid id, Guid classId, SpaceClassRequest request) => PutAsync<bool>($"/organizations/{id}/classes/{classId}", request);
    public Task<ApiResult<bool>> ArchiveClassAsync(Guid id, Guid classId) => DeleteAsync<bool>($"/organizations/{id}/classes/{classId}");
    public Task<ApiResult<List<ClassTeacher>>> AssignTeacherAsync(Guid id, Guid classId, AssignTeacherRequest request) =>
        PutAsync<List<ClassTeacher>>($"/organizations/{id}/classes/{classId}/teachers", request);
    public Task<ApiResult<bool>> UnassignTeacherAsync(Guid id, Guid classId, Guid teacherId, string? subject) =>
        DeleteAsync<bool>($"/organizations/{id}/classes/{classId}/teachers/{teacherId}{Q(("subject", subject))}");

    public Task<ApiResult<List<SpaceStudent>>> StudentsAsync(Guid id, Guid? classId = null, StudentStatus? status = null) =>
        GetAsync<List<SpaceStudent>>($"/organizations/{id}/students{Q(("classId", classId), ("status", status))}");
    public Task<ApiResult<SpaceStudent>> AddStudentAsync(Guid id, StudentRequest request) => PostAsync<SpaceStudent>($"/organizations/{id}/students", request);
    public Task<ApiResult<SpaceStudent>> UpdateStudentAsync(Guid id, Guid studentId, StudentRequest request) =>
        PutAsync<SpaceStudent>($"/organizations/{id}/students/{studentId}", request);
    public Task<ApiResult<SpaceStudent>> SetStudentStatusAsync(Guid id, Guid studentId, StudentStatus status) =>
        PostAsync<SpaceStudent>($"/organizations/{id}/students/{studentId}/status", new { status });
    public Task<ApiResult<bool>> DeleteStudentAsync(Guid id, Guid studentId) => DeleteAsync<bool>($"/organizations/{id}/students/{studentId}");
    public Task<ApiResult<ImportResult>> ImportStudentsAsync(Guid id, string csv, bool commit) =>
        PostAsync<ImportResult>($"/organizations/{id}/students/import", new { csv, commit });
    public Task<ApiResult<ImportResult>> ImportStudentsFileAsync(Guid id, ImageUpload file) =>
        UploadAsync<ImportResult>($"/organizations/{id}/students/import-file", file);

    public Task<ApiResult<List<AccessCodeInfo>>> CodesAsync(Guid id, Guid? classId = null, AccessCodeKind? kind = null) =>
        GetAsync<List<AccessCodeInfo>>($"/organizations/{id}/codes{Q(("classId", classId), ("kind", kind))}");
    public Task<ApiResult<SpaceStudent>> IssueStudentCodeAsync(Guid id, Guid studentId) => PostAsync<SpaceStudent>($"/organizations/{id}/students/{studentId}/code");
    public Task<ApiResult<List<AccessCodeInfo>>> IssueClassCodesAsync(Guid id, Guid classId) => PostAsync<List<AccessCodeInfo>>($"/organizations/{id}/classes/{classId}/codes");
    public Task<ApiResult<List<AccessCodeInfo>>> IssueBlankCodesAsync(Guid id, Guid classId, int count) =>
        PostAsync<List<AccessCodeInfo>>($"/organizations/{id}/codes/blank", new { classroomId = classId, count });
    public Task<ApiResult<AccessCodeInfo>> IssueTeacherCodeAsync(Guid id) => PostAsync<AccessCodeInfo>($"/organizations/{id}/codes/teacher");
    public Task<ApiResult<bool>> RevokeCodeAsync(Guid id, Guid codeId) => DeleteAsync<bool>($"/organizations/{id}/codes/{codeId}");

    public Task<ApiResult<CodeInfo>> CheckCodeAsync(string code) => PostAsync<CodeInfo>("/auth/codes/check", new { code });
    public Task<ApiResult<AuthResponse>> ActivateCodeAsync(ActivateCodeRequest request) => PostAsync<AuthResponse>("/auth/codes/activate", request);
    public Task<ApiResult<RedeemCodeResult>> RedeemCodeAsync(string code) => PostAsync<RedeemCodeResult>("/access-codes/redeem", new { code });
}
