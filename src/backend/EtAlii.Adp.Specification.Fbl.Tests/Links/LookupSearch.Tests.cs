using System.Diagnostics;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Links;

/// <summary>
/// The lookup's search over a workspace (DISL 0.12 §9.4, etalii.adp spec 092, T020, FR-012, SC-006): by name as typed, each
/// element with its document's name and folder and the link that names it, narrowed to the action's <c>origins</c>.
/// </summary>
public class LookupSearchTests
{
    private const string Root = "/w";

    [Fact]
    public void ASearch_FindsElementsWhoseNameHoldsWhatIsTyped_ThoseItBeginsFirst()
    {
        // Arrange.
        var workspace = new LinkWorkspace(Root, [
            Document("/w/plans/roadmap.tml", "r1r1r1r1", "generic/timeline", ("launch", "Launch"), ("prelaunch", "A pre-launch review"), ("beta", "Beta")),
            Document("/w/features.yaml", "f2f2f2f2", "etalii/knowledge", ("signin", "Sign-in"), ("relaunch", "Relaunch the shop")),
        ]);

        // Act.
        var found = workspace.Search("LAUNCH");

        // Assert.
        Assert.Equal(["Launch", "A pre-launch review", "Relaunch the shop"], found.Select(candidate => candidate.Name));
        var first = found[0];
        Assert.Equal("roadmap.tml", first.DocumentName);
        Assert.Equal("plans", first.Folder);
        Assert.Equal("launch", first.ElementId);
        Assert.Equal("$r1r1r1r1@roadmap.tml#launch=Launch", first.Link);
        Assert.Equal("", found[2].Folder);
        Assert.True(workspace.Resolve(first.Link).Resolved);
    }

    [Fact]
    public void ASearch_IsNarrowedToTheOrigins_AndSkipsDocumentsWithoutAnId()
    {
        // Arrange.
        var workspace = new LinkWorkspace(Root, [
            Document("/w/roadmap.tml", "r1r1r1r1", "generic/timeline", ("launch", "Launch")),
            Document("/w/features.yaml", "f2f2f2f2", "etalii/knowledge", ("launchpad", "Launchpad")),
            Document("/w/draft.tml", null, "generic/timeline", ("launch2", "Launch again")),
        ]);

        // Act.
        var everywhere = workspace.Search("launch");
        var knowledge = workspace.Search("launch", ["etalii/knowledge"]);

        // Assert.
        Assert.Equal(["Launch", "Launchpad"], everywhere.Select(candidate => candidate.Name));
        Assert.Equal(["Launchpad"], knowledge.Select(candidate => candidate.Name));
    }

    [Fact]
    public void ASearch_SkipsElementsThatAreLinksThemselves()
    {
        // Arrange: the mind map's second node links to the roadmap's launch, and is named as its target is.
        var map = Document("/w/ideas.mm", "m3m3m3m3", "freeplane/mindmap", ("root", "Ideas"), ("n1", "Launch")) with
        {
            Links = [new HeldLink("n1", "$r1r1r1r1@roadmap.tml#launch=Launch")],
        };
        var workspace = new LinkWorkspace(Root, [Document("/w/roadmap.tml", "r1r1r1r1", "generic/timeline", ("launch", "Launch")), map]);

        // Act.
        var found = workspace.Search("launch");

        // Assert: the launch itself, and not the node that links to it, which a link to it would make a link to a link.
        Assert.Equal(["roadmap.tml"], found.Select(candidate => candidate.DocumentName));
    }

    [Fact]
    public void AnEmptySearch_OffersEveryElement_UpToTheLimit()
    {
        // Arrange.
        var workspace = new LinkWorkspace(Root, [Document("/w/roadmap.tml", "r1r1r1r1", null, ("a", "Alpha"), ("b", "Beta"), ("c", "Gamma"))]);

        // Act.
        var found = workspace.Search("", limit: 2);

        // Assert.
        Assert.Equal(["Alpha", "Beta"], found.Select(candidate => candidate.Name));
    }

    [Fact]
    public void ASearchOverTwoHundredDocuments_AnswersWellWithinAKeystroke()
    {
        // Arrange: SC-006's workspace of 200 documents, each with 50 elements.
        var workspace = new LinkWorkspace(Root, Enumerable.Range(0, 200).Select(index => Document(
            $"/w/folder{index % 10}/document{index}.tml", $"d{index:0000000}", "generic/timeline",
            [.. Enumerable.Range(0, 50).Select(element => ($"e{element}", $"Element {element} of document {index}"))])));
        workspace.Search("warm up");
        var clock = Stopwatch.StartNew();

        // Act.
        var found = workspace.Search("of document 1");

        // Assert.
        clock.Stop();
        Assert.Equal(50, found.Count);
        Assert.True(clock.ElapsedMilliseconds < 250, $"The search took {clock.ElapsedMilliseconds} ms.");
    }

    private static IndexedDocument Document(string path, string? id, string? origin, params (string Id, string Name)[] elements) =>
        new(path, id, "Name", [.. elements.Select(element => new IndexedElement(element.Id, [new KeyValuePair<string, string>("Name", element.Name)]))], []) { Origin = origin };
}
