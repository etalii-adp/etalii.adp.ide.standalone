using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// Reading an activity file (agent-activity-diagram Requirements 2.1 to 2.5, 2.8, 2.9): every
/// field the file holds reaches the model, and a file nobody edited is written back as it was.
/// </summary>
public sealed class AadDocumentTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static AadDocumentEntry OneDay() => AadDocumentEntry.Read(Fixture("one-day.aad"));

    [Fact]
    public void AFileNobodyEdited_IsWrittenBackByteForByte()
    {
        var text = Fixture("one-day.aad");

        var entry = AadDocumentEntry.Read(text);

        Assert.True(entry.IsUsable, entry.Unreadable);
        Assert.Equal(text, entry.Document.Text);
    }

    [Fact]
    public void EveryElement_IsReadWithItsFields()
    {
        var model = OneDay().Model;

        Assert.Equal(
            ["Project p-standalone", "Project p-adp", "Specification s-knowledge", "Specification s-rider", "Agent a-dev2", "Location l-kd", "Environment e-fractal"],
            model.Elements.Select(element => $"{element.Kind} {element.Id}"));

        var specification = model.Elements.Single(element => element.Id == "s-knowledge");
        Assert.Equal("Knowledge designer", specification.Name);
        Assert.Equal("progressing", specification.Status);
        Assert.Equal("Progressing", specification.StatusLabel);
        Assert.Equal(".spec-workflow/specs/knowledge-designer/requirements.md", specification.Link);

        var location = model.Elements.Single(element => element.Id == "l-kd");
        Assert.Equal("features/knowledge-designer", location.Name);
        Assert.Equal(".claude/worktrees/kd", location.Folder);
        Assert.Equal("C:/git/etalii.adp.ide.standalone/.claude/worktrees/kd", location.FolderLink);
        Assert.StartsWith("https://github.com/", location.BranchLink, StringComparison.Ordinal);

        Assert.Equal("Local machine", model.Elements.Single(element => element.Id == "e-fractal").KindLabel);
        Assert.Equal("archived", model.Elements.Single(element => element.Id == "s-rider").Status);
    }

    [Fact]
    public void TasksAndPullRequests_AreRowsOfTheirElement_NotElements()
    {
        var model = OneDay().Model;

        var tasks = model.Elements.Single(element => element.Id == "s-knowledge").Rows;
        Assert.Equal(["t-k2", "t-k6"], tasks.Select(row => row.Id));
        Assert.Equal(new AadRow("t-k6", "Table component", "inputRequired", "2026-10-08T21:10:00+02:00", "https://github.com/etalii-adp/etalii.adp.ide.standalone/pull/150"), tasks[1]);

        var pullRequests = model.Elements.Single(element => element.Id == "l-kd").Rows;
        Assert.Equal("150: Knowledge designer, tasks 2 and 6", Assert.Single(pullRequests).Title);
        Assert.Equal("", pullRequests[0].Status);

        Assert.DoesNotContain(model.Elements, element => element.Id is "t-k2" or "pr-150");
    }

    [Fact]
    public void ARelation_IsAKeyOnTheElementAtItsOneEnd()
    {
        var model = OneDay().Model;

        Assert.Equal(
            [
                "ProjectSpecification p-standalone > s-knowledge",
                "ProjectSpecification p-standalone > s-rider",
                "SpecificationAgent s-knowledge > a-dev2",
                "AgentLocation a-dev2 > l-kd",
                "LocationEnvironment l-kd > e-fractal",
            ],
            model.Relations.Select(relation => $"{relation.Type} {relation.From} > {relation.To}"));
        Assert.Equal("specification:a-dev2", model.Relations.Single(relation => relation.Type == "SpecificationAgent").Id);
    }

    [Fact]
    public void TheReadersOwnPart_IsReadBesideTheModel()
    {
        var model = OneDay().Model;

        Assert.False(model.ShowArchived);
        Assert.Equal(new AadPlacement(240, -80), Assert.Single(model.Placements).Value);
        Assert.Equal("s-knowledge", model.Placements.Keys.Single());

        // The file unfolds Pending for this specification; Finished keeps its default, folded.
        Assert.Equal(["finished"], model.Collapsed["s-knowledge"]);
        // A specification nobody touched has the defaults.
        Assert.Equal(["finished", "pending"], model.Collapsed["s-rider"].Order(StringComparer.Ordinal));
        Assert.Equal([AadDefinition.PullRequestsGroup], model.Collapsed["l-kd"]);
    }

    [Fact]
    public void AFolderLeftOut_IsTheMainCheckout()
    {
        var entry = AadDocumentEntry.Read("agent-activity-diagram: 1\r\nlocations:\r\n  - id: l1\r\n    branch: develop\r\n");

        Assert.Equal("Default", Assert.Single(entry.Model.Elements).Folder);
    }

    [Fact]
    public void ANewFile_IsTheHeaderAlone_AndReadsEmptyWithoutAFinding()
    {
        var text = new AadDocumentFactory().CreateEmptyDocument("team");

        var entry = AadDocumentEntry.Read(text);

        Assert.Equal("agent-activity-diagram: 1\r\n", text);
        Assert.True(entry.IsUsable, entry.Unreadable);
        Assert.Empty(entry.Model.Elements);
        Assert.Empty(entry.Document.Model.Findings);
    }

    [Fact]
    public void AFileThatIsNotAnActivityFile_IsHeldUnreadable_WithTheReadersReason()
    {
        var entry = AadDocumentEntry.Read("projects: [unterminated\r\n");

        Assert.False(entry.IsUsable);
        Assert.NotEqual("", entry.Unreadable);
        Assert.Empty(entry.Model.Elements);
    }
}
