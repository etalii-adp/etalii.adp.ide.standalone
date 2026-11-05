using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>The three forms of a link and the conversions between them (Requirement 12.3), and the rewrite when a map moves (12.14).</summary>
public class MindmapLinksTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("mindmap-links");
    private readonly string _root;

    public MindmapLinksTests()
    {
        _root = _testFolder.Path;
        Directory.CreateDirectory(IoPath.Combine(_root, "docs"));
        Directory.CreateDirectory(IoPath.Combine(_root, "src", "backend"));
        File.WriteAllText(IoPath.Combine(_root, "src", "backend", "Service.cs"), "");
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    private string MapAt(string folder) => IoPath.Combine(_root, folder, "map.mm");

    [Fact]
    public void ToMapRelative_ProducesFreeplanesForm_ForwardSlashesFromTheMapsFolder()
    {
        // Act.
        var stored = MindmapLinks.ToMapRelative(MapAt("docs"), ["src", "backend", "Service.cs"], ProjectRoot.From(_root));

        // Assert.
        Assert.Equal("../src/backend/Service.cs", stored);
    }

    [Fact]
    public void ResolveWithinProject_TurnsAStoredLinkBackIntoTheFile()
    {
        // Act.
        var resolved = MindmapLinks.ResolveWithinProject(MapAt("docs"), "../src/backend/Service.cs", ProjectRoot.From(_root));

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "src", "backend", "Service.cs"), resolved?.FullPath);
    }

    [Fact]
    public void ResolveWithinProject_RefusesALinkThatLeavesTheProject()
    {
        // Arrange, act and assert.
        // Requirement 12.9: however a link got into the file, it never reaches outside.
        Assert.Null(MindmapLinks.ResolveWithinProject(MapAt("docs"), "../../../etc/passwd", ProjectRoot.From(_root)));
    }

    [Fact]
    public void ResolveWithinProject_RefusesALinkToTheProjectRootItself()
    {
        // Arrange, act and assert.
        // The root is the project, not a file a link can open (code-structure FR-059: the check
        // moved onto ProjectRoot unchanged).
        Assert.Null(MindmapLinks.ResolveWithinProject(MapAt("docs"), "..", ProjectRoot.From(_root)));
    }

    [Fact]
    public void ResolveWithinProject_LeavesAUrlAlone()
    {
        // Arrange, act and assert.
        Assert.True(MindmapLinks.IsExternal("https://www.freeplane.org/"));
        Assert.Null(MindmapLinks.ResolveWithinProject(MapAt("docs"), "https://www.freeplane.org/", ProjectRoot.From(_root)));
    }

    [Fact]
    public void ProjectRelative_SplitsIntoSegments()
    {
        // Arrange, act and assert.
        Assert.Equal(["src", "backend", "Service.cs"], MindmapLinks.ProjectRelative(IoPath.Combine(_root, "src", "backend", "Service.cs"), ProjectRoot.From(_root)));
    }

    [Fact]
    public void Rebase_RewritesEveryFileLink_WhenTheMapChangesFolder()
    {
        // Arrange.
        var document = MindmapDocument.Parse(
            "<map version=\"freeplane 1.11.5\"><node TEXT=\"r\" ID=\"r\" LINK=\"../src/backend/Service.cs\">" +
            "<node TEXT=\"a\" ID=\"a\" LINK=\"https://example.org/\"/><node TEXT=\"b\" ID=\"b\"/></node></map>");

        // Act.
        var rewritten = MindmapLinks.Rebase(document, MapAt("docs"), MapAt(IoPath.Combine("src", "backend")));

        // Assert.
        Assert.Equal(1, rewritten);
        Assert.Equal("Service.cs", document.Root.Link);
        Assert.Equal("https://example.org/", document.Find("a")!.Link); // external, untouched
        Assert.Null(document.Find("b")!.Link);
    }

    [Fact]
    public void Rebase_IsANoOpForARenameWithinTheFolder()
    {
        // Arrange.
        var document = MindmapDocument.Parse("<map version=\"freeplane 1.11.5\"><node TEXT=\"r\" ID=\"r\" LINK=\"../x.cs\"/></map>");

        // Act.
        var rewritten = MindmapLinks.Rebase(document, MapAt("docs"), IoPath.Combine(_root, "docs", "renamed.mm"));

        // Assert.
        Assert.Equal(0, rewritten);
        Assert.Equal("../x.cs", document.Root.Link);
    }
}
