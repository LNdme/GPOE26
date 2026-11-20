using Cours.Model;
using GPOE26.Workspaces.Abstractions;

namespace Cours.Helpers;

public static class WorkspaceCourseQueries
{
    public static bool CanManageCourses(this WorkspaceTokenContext workspace) =>
        workspace.HasAnyRole(
            WorkspaceRoles.Owner,
            WorkspaceRoles.Admin,
            WorkspaceRoles.Guardian,
            WorkspaceRoles.Instructor);

    public static IQueryable<Course> OwnedByInWorkspace(
        this IQueryable<Course> query,
        Guid ownerId,
        WorkspaceTokenContext workspace) =>
        query.Where(course =>
            course.OwnerId == ownerId &&
            (course.WorkspaceId == workspace.WorkspaceId ||
             (workspace.IsPersonal && course.WorkspaceId == null)));

    public static bool IsInWorkspace(this Course course, WorkspaceTokenContext workspace) =>
        course.WorkspaceId == workspace.WorkspaceId ||
        (workspace.IsPersonal && course.WorkspaceId is null);
}
