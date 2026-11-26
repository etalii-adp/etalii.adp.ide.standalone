using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Links;

/// <summary>
/// Resolution of links (FBL 17.3) and the link load-time validation writes (17.4, step 4), replayed against every link of the
/// conformance fixture <c>links-workspace</c>: each resolves to the target and element <c>conformance.json</c> names and is
/// written as its <c>after</c>, or gives its finding with its candidates (etalii.adp spec 092, T013).
/// </summary>
public class LinkResolutionTests
{
    /// <summary>The links of the fixture fewer than which means its reading broke.</summary>
    private const int MinimumLinks = 30;

    private static readonly LinkWorkspace Workspace = LinksWorkspaceFixture.Workspace();

    public static TheoryData<int> Links()
    {
        var data = new TheoryData<int>();
        for (var index = 0; index < Entries().Count; index++) data.Add(index);
        return data;
    }

    [Fact]
    public void TheFixtureIsRead()
    {
        Assert.True(Entries().Count >= MinimumLinks, $"Only {Entries().Count} links were read from conformance.json.");
        Assert.Equal(11, Workspace.Documents.Count);
    }

    [Theory]
    [MemberData(nameof(Links))]
    public void EveryLink_ResolvesAsTheFixtureSays(int index)
    {
        // Arrange.
        var entry = Entries()[index];
        var text = entry.GetProperty("link").GetString()!;
        var what = $"{entry.GetProperty("in").GetString()} {entry.GetProperty("at").GetString()} ({entry.GetProperty("case").GetString()})";

        // Act.
        var resolution = Workspace.Resolve(text);

        // Assert.
        if (entry.TryGetProperty("finding", out var finding))
        {
            Assert.True(resolution.Finding is not null, $"{what} resolved, where the fixture says {finding.GetString()}.");
            Assert.Equal(finding.GetString(), resolution.Finding!.Code);
            var candidates = entry.TryGetProperty("candidates", out var listed) ? listed.EnumerateArray().Select(candidate => LinksWorkspaceFixture.PathOf(candidate.GetString()!)).ToList() : null;
            if (candidates is not null) Assert.Equal(candidates, resolution.Finding.Candidates);
            return;
        }
        Assert.True(resolution.Resolved, $"{what} gave {resolution.Finding?.Code}: {resolution.Finding?.Message}");
        Assert.Equal(LinksWorkspaceFixture.PathOf(entry.GetProperty("target").GetString()!), resolution.Document!.Path);
        Assert.Equal(entry.TryGetProperty("element", out var element) ? element.GetString() : null, resolution.Element?.Id);
        Assert.Equal(entry.GetProperty("after").GetString(), resolution.Refreshed!.Text);
    }

    [Fact]
    public void ADocumentWithoutAnId_IsReachedByNoLink()
    {
        var withoutId = JsonDocument.Parse(File.ReadAllText(LinksWorkspaceFixture.Conformance)).RootElement.GetProperty("withoutId").EnumerateArray().Select(path => LinksWorkspaceFixture.PathOf(path.GetString()!)).ToList();

        Assert.Equal(withoutId, Workspace.Documents.Where(document => document.DocumentId is null).Select(document => document.Path));
    }

    [Fact]
    public void AFindingNamesTheCandidatesByTheirPathInTheWorkspace()
    {
        var finding = Workspace.Resolve("$r0a6m3xy@roadmap.tml#q1=First quarter").Finding!;

        Assert.Equal("2 documents are 'roadmap.tml' with id 'r0a6m3xy': archive/roadmap.tml, plan/roadmap.tml.", finding.Message);
    }

    [Fact]
    public void AnElementsBacklinks_AreTheLinksOfTheWorkspaceThatResolveToIt()
    {
        var backlinks = Workspace.Backlinks(LinksWorkspaceFixture.PathOf("plan/release.tml"), "launch");

        Assert.Equal([("ideas/ideas.mm", "ID_2"), ("teams/services.dgr", "l1")], backlinks.Select(backlink => (Workspace.Display(backlink.Path), backlink.Holder)));
        Assert.Equal(3, Workspace.BrokenByDeleting(LinksWorkspaceFixture.PathOf("plan/release.tml"), ["launch", "discovery"]));
    }

    [Fact]
    public void ADocumentsDependents_AreTheOthersThatHoldALinkNamingIt_ByItsIdOrItsFileName()
    {
        var release = LinksWorkspaceFixture.PathOf("plan/release.tml");

        var dependents = Workspace.Dependents(release);
        var gone = new LinkWorkspace(LinksWorkspaceFixture.PathOf(""), Workspace.Documents.Where(document => document.Path != release)).Dependents(release);

        Assert.Contains("ideas/ideas.mm", dependents.Select(Workspace.Display));
        Assert.Contains("teams/services.dgr", dependents.Select(Workspace.Display));
        Assert.DoesNotContain(release, dependents);
        // Once it is gone its id is not known, and its file name still names it.
        Assert.Equal(dependents, gone);
    }

    private static List<JsonElement> Entries() =>
        [.. JsonDocument.Parse(File.ReadAllText(LinksWorkspaceFixture.Conformance)).RootElement.GetProperty("links").EnumerateArray()];
}
