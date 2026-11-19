using GPOE26.Agent.Abstractions;

namespace GPOE26.Tests;

public sealed class AgentProfileRoutingTests
{
    [Fact]
    public void DefaultCatalogContainsAllBuiltInProfiles()
    {
        var catalog = AgentProfileCatalog.CreateDefault();

        var ids = catalog.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(5, ids.Count);
        Assert.Contains(AgentProfileIds.Pedagogical, ids);
        Assert.Contains(AgentProfileIds.Research, ids);
        Assert.Contains(AgentProfileIds.ScientificExplanation, ids);
        Assert.Contains(AgentProfileIds.CourseAuthor, ids);
        Assert.Contains(AgentProfileIds.Personal, ids);
    }

    [Theory]
    [InlineData(AgentProfileIds.Pedagogical)]
    [InlineData(AgentProfileIds.Research)]
    [InlineData(AgentProfileIds.ScientificExplanation)]
    [InlineData(AgentProfileIds.CourseAuthor)]
    [InlineData(AgentProfileIds.Personal)]
    public void RouterSelectsRequestedProfile(string profileId)
    {
        var catalog = AgentProfileCatalog.CreateDefault();
        var router = new AgentProfileRouter(catalog);

        Assert.Equal(profileId, router.Resolve(profileId).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-profile")]
    public void RouterFallsBackToPedagogicalProfile(string? profileId)
    {
        var router = new AgentProfileRouter(AgentProfileCatalog.CreateDefault());

        Assert.Equal(AgentProfileIds.Pedagogical, router.Resolve(profileId).Id);
    }

    [Fact]
    public void CatalogRejectsDuplicateProfileIdsIgnoringCase()
    {
        var profile = new AgentProfileDefinition("sample", "Sample", "Sample", "Sample", []);

        Assert.Throws<ArgumentException>(() => new AgentProfileCatalog(
        [
            profile,
            profile with { Id = "SAMPLE" }
        ]));
    }

    [Fact]
    public void RouterSupportsProfilesAddedToAnExtendedCatalog()
    {
        var profile = new AgentProfileDefinition("custom", "Custom", "Custom", "Custom instructions", []);
        var router = new AgentProfileRouter(new AgentProfileCatalog([profile]), "custom");

        Assert.Same(profile, router.Resolve("custom"));
        Assert.Same(profile, router.Resolve(null));
    }
}
