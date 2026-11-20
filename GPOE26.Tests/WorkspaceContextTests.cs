using System.Security.Claims;
using GPOE26.Workspaces.Abstractions;

namespace GPOE26.Tests;

public sealed class WorkspaceContextTests
{
    [Fact]
    public void TryGetWorkspaceContextParsesWorkspaceAndMultipleRoles()
    {
        var workspaceId = Guid.NewGuid();
        var principal = Principal(
            new(WorkspaceClaimTypes.Id, workspaceId.ToString()),
            new(WorkspaceClaimTypes.Kind, WorkspaceKinds.Family),
            new(WorkspaceClaimTypes.Role, WorkspaceRoles.Owner),
            new(WorkspaceClaimTypes.Role, WorkspaceRoles.Guardian));

        Assert.True(principal.TryGetWorkspaceContext(out var context));
        Assert.Equal(workspaceId, context!.WorkspaceId);
        Assert.False(context.IsPersonal);
        Assert.True(context.HasAnyRole(WorkspaceRoles.Owner, WorkspaceRoles.Guardian));
    }

    [Theory]
    [MemberData(nameof(InvalidWorkspaceClaims))]
    public void TryGetWorkspaceContextRejectsInvalidClaims(Claim[] claims)
    {
        Assert.False(Principal(claims).TryGetWorkspaceContext(out _));
    }

    public static IEnumerable<object[]> InvalidWorkspaceClaims()
    {
        var id = Guid.NewGuid().ToString();
        yield return new object[] { new[] { new Claim(WorkspaceClaimTypes.Kind, WorkspaceKinds.Personal) } };
        yield return new object[]
        {
            new[]
            {
                new Claim(WorkspaceClaimTypes.Id, id),
                new Claim(WorkspaceClaimTypes.Id, Guid.NewGuid().ToString()),
                new Claim(WorkspaceClaimTypes.Kind, WorkspaceKinds.Personal),
                new Claim(WorkspaceClaimTypes.Role, WorkspaceRoles.Owner)
            }
        };
        yield return new object[]
        {
            new[]
            {
                new Claim(WorkspaceClaimTypes.Id, id),
                new Claim(WorkspaceClaimTypes.Kind, "Unknown"),
                new Claim(WorkspaceClaimTypes.Role, WorkspaceRoles.Owner)
            }
        };
        yield return new object[]
        {
            new[]
            {
                new Claim(WorkspaceClaimTypes.Id, id),
                new Claim(WorkspaceClaimTypes.Kind, WorkspaceKinds.Personal),
                new Claim(WorkspaceClaimTypes.Role, "Unknown")
            }
        };
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));
}
