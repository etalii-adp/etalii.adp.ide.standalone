using EtAlii.Adp.Diagram.Wire;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Toolbox panel renders whatever <c>DescribeToolbox</c> answers, so this proves the
/// answer over the real host: a registered mindmap yields the module's palette, and the
/// entry's drop action id names the same add-child action the menu and the keyboard run -
/// one implementation behind every trigger (tech.md's "Specifying a diagram type").
/// </summary>
public class DiagramToolboxFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        // A registered mindmap, and a plain file no diagram type claims.
        File.WriteAllText(IoPath.Combine(projectFolder, "roadmap.adp"), "freeplane/mindmap\n");
        File.WriteAllText(
            IoPath.Combine(projectFolder, "roadmap.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"roadmap\" ID=\"ID_1\"/>\n</map>\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "notes.txt"), "not a diagram\n");
        // The shape the combined examples project actually uses: a registration in a subfolder
        // whose name contains a space. Documentation task 9's manual pass found the Toolbox
        // panel empty for exactly this shape and named the space as a candidate cause; the test
        // below rules that out at this seam (the defect was the canvas never registering).
        var subfolder = IoPath.Combine(projectFolder, "example 1");
        Directory.CreateDirectory(subfolder);
        File.WriteAllText(IoPath.Combine(subfolder, "tea.adp"), "wardley/map\n");
        File.WriteAllText(IoPath.Combine(subfolder, "tea.owm"), "anchor Need [0.9, 0.5]\ncomponent Thing [0.4, 0.4]\n");
        return projectFolder;
    }

    [Fact]
    public async Task AMindmapAnswersWithTheModulesPalette_AndItsDropRunsTheAddChildAction()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var response = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("roadmap.adp") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        var item = Assert.Single(response.Items);
        Assert.Equal("Node", item.Label);
        Assert.Equal("mindmap.add-child", item.DropActionId);
        Assert.NotEmpty(item.Icon);
        Assert.NotEmpty(item.Description);
    }

    [Fact]
    public async Task AFileNoDiagramTypeClaims_AnswersWithAnEmptyPalette()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        // Act: unroutable and non-existent both answer the same, non-revealing way.
        var unroutable = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("notes.txt") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var missing = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("gone.adp") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(unroutable.Items);
        Assert.Empty(missing.Items);
    }

    [Fact]
    public async Task AWardleyMapInASubfolderWithASpace_AnswersWithTheModulesPalette()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        var path = Path.Of("example 1", "tea.adp");

        // Act.
        var response = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = path },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the module's whole palette, unimpressed by where the document lives.
        Assert.Equal(
            ["Component", "Anchor", "Market", "Ecosystem", "Submap", "Pipeline", "Note", "Annotation"],
            response.Items.Select(item => item.Label));
        Assert.DoesNotContain(response.Items, item => item.DropActionId.Length == 0);
    }

    private static Path PathOf(string fileName)
    {
        var path = Path.Of(fileName);
        return path;
    }
}
