using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The pair of files a diagram may be, and what the rename, delete and create commands do
/// with it: both move, both go, both appear, or neither does (mindmap-diagram Requirement 2).
/// </summary>
public class DiagramFilePairTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", ".mm");
    private static readonly DiagramDefinition ClassDiagram = new(new DiagramOrigin("uml", "class"), "Class diagram");

    private readonly string _root;
    private readonly IHistoryStack _history;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap, ClassDiagram);

    public DiagramFilePairTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _, Mindmap, ClassDiagram);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteRegistration(string baseName, DiagramDefinition definition)
    {
        var path = IoPath.Combine(_root, baseName + ".adp");
        File.WriteAllText(path, definition.Origin.MimeType + "\n");
        return path;
    }

    // ---- the pair itself ----------------------------------------------------------------

    [Fact]
    public void SiblingOf_ARegistrationWhoseTypeKeepsABody_NamesTheSibling()
    {
        var adp = WriteRegistration("domain", Mindmap);

        Assert.Equal(IoPath.Combine(_root, "domain.mm"), DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_ARegistrationWhoseTypeKeepsNoBody_IsNull()
    {
        var adp = WriteRegistration("classes", ClassDiagram);

        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_AnUnknownMimeType_IsNull()
    {
        var adp = IoPath.Combine(_root, "mystery.adp");
        File.WriteAllText(adp, "nobody/knows\n");

        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_APlainFile_IsNull()
    {
        var txt = IoPath.Combine(_root, "notes.txt");
        File.WriteAllText(txt, "freeplane/mindmap\n"); // the content is not what makes a registration file

        Assert.Null(DiagramFilePair.SiblingOf(txt, _catalog));
    }

    // ---- create: both or neither -----------------------------------------------------

    [Fact]
    public async Task Create_WithASibling_WritesBothFiles()
    {
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");

        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("freeplane/mindmap\n", File.ReadAllText(IoPath.Combine(_root, "domain.adp")));
        Assert.Equal("<map/>", File.ReadAllText(IoPath.Combine(_root, "domain.mm")));
    }

    [Fact]
    public async Task Create_WhenTheSiblingNameIsTaken_LeavesNeitherBehind()
    {
        // The registration file would have been created fine; the sibling's name is what
        // collides. A half-created pair must never be observable (Requirement 1.6).
        File.WriteAllText(IoPath.Combine(_root, "domain.mm"), "someone else's map");
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");

        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp")), "the registration file was left behind");
        Assert.Equal("someone else's map", File.ReadAllText(IoPath.Combine(_root, "domain.mm")));
        Assert.Empty(Directory.GetFiles(_root, "~adp-*"));
    }

    [Fact]
    public async Task Create_WithASibling_UndoRemovesBoth()
    {
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");
        await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")));
    }

    [Fact]
    public async Task Create_WithoutASibling_BehavesExactlyAsBefore()
    {
        var command = new CreateDiagramFileCommand(_root, "classes.adp", "uml/class");

        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(Directory.GetFiles(_root));
    }

    // ---- rename: the pair stays a pair ---------------------------------------------

    [Fact]
    public async Task Rename_ARegistrationFile_RenamesItsSiblingWithIt()
    {
        var adp = WriteRegistration("domain", Mindmap);
        File.WriteAllText(IoPath.Combine(_root, "domain.mm"), "<map/>");

        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.adp")));
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.mm")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")));
    }

    [Fact]
    public async Task Rename_ARegistrationFile_UndoRestoresBothNames()
    {
        var adp = WriteRegistration("domain", Mindmap);
        File.WriteAllText(IoPath.Combine(_root, "domain.mm"), "<map/>");
        await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.True(File.Exists(IoPath.Combine(_root, "domain.mm")));
        Assert.False(File.Exists(IoPath.Combine(_root, "renamed.mm")));
    }

    [Fact]
    public async Task Rename_WhenTheSiblingsNewNameIsTaken_ChangesNothing()
    {
        var adp = WriteRegistration("domain", Mindmap);
        File.WriteAllText(IoPath.Combine(_root, "domain.mm"), "<map/>");
        File.WriteAllText(IoPath.Combine(_root, "renamed.mm"), "in the way");

        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("renamed.mm", result.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(adp), "the registration file was moved although its sibling could not follow");
        Assert.Equal("in the way", File.ReadAllText(IoPath.Combine(_root, "renamed.mm")));
    }

    [Fact]
    public async Task Rename_ARegistrationWhoseBodyIsMissing_RenamesTheRegistrationAlone()
    {
        // Requirement 2.12: a missing body is a recoverable state, not a broken diagram.
        var adp = WriteRegistration("domain", Mindmap);

        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.adp")));
    }

    // ---- delete: the pair goes together --------------------------------------------

    [Fact]
    public async Task Delete_ARegistrationFile_DeletesItsSiblingWithIt()
    {
        var adp = WriteRegistration("domain", Mindmap);
        File.WriteAllText(IoPath.Combine(_root, "domain.mm"), "<map/>");

        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(adp));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")), "the body was orphaned");
    }

    [Fact]
    public async Task Delete_ARegistrationWithNoBody_DeletesJustTheRegistration()
    {
        var adp = WriteRegistration("classes", ClassDiagram);

        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Directory.GetFiles(_root));
    }

    // ---- body: and view: headers (c4-diagrams Requirement 2.4) --------------------------

    /// <summary>A registration naming a body it does not own, the way a C4 view does.</summary>
    private string WriteRegistrationNamingBody(string baseName, DiagramDefinition definition, string body, string? view = null)
    {
        var path = IoPath.Combine(_root, baseName + ".adp");
        var text = definition.Origin.MimeType + "\n" + "body: " + body + "\n" + (view is null ? "" : "view: " + view + "\n");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void BodyOf_NoHeaders_IsTheDerivedSibling_AndIsOwned()
    {
        // The case every existing type is in, which must behave exactly as it always has.
        var adp = WriteRegistration("domain", Mindmap);

        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "domain.mm"), body!.Value.Path);
        Assert.True(body.Value.IsOwned);
        Assert.Null(body.Value.ViewKey);
    }

    [Fact]
    public void BodyOf_ABodyHeader_ResolvesAgainstTheProjectRoot_AndIsNotOwned()
    {
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm", "containers");

        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "shared", "model.mm"), body!.Value.Path);
        Assert.Equal("containers", body.Value.ViewKey);
        Assert.False(body.Value.IsOwned);
    }

    [Fact]
    public void BodyOf_AViewHeaderWithoutABody_StillNamesTheView_AndStaysOwned()
    {
        var path = IoPath.Combine(_root, "solo.adp");
        File.WriteAllText(path, Mindmap.Origin.MimeType + "\nview: context\n");

        var body = DiagramFilePair.BodyOf(path, _catalog, _root);

        Assert.NotNull(body);
        Assert.Equal("context", body!.Value.ViewKey);
        Assert.True(body.Value.IsOwned);
    }

    [Theory]
    [InlineData("../outside.mm")]
    [InlineData("nested/../../outside.mm")]
    // Rooted on both platforms, so the refusal is asserted the same way everywhere. A
    // Windows-style "C:\..." is deliberately not a case here: it is rooted on Windows and an
    // ordinary relative file name on Linux, so it would assert two different things.
    [InlineData("/etc/passwd")]
    public void BodyOf_ABodyHeaderEscapingTheProject_IsRefused(string escaping)
    {
        // The header is user-editable text; following it anywhere on disk would turn an .adp
        // file into a way to read arbitrary files through the backend.
        var adp = WriteRegistrationNamingBody("escape", Mindmap, escaping);

        Assert.Null(DiagramFilePair.BodyOf(adp, _catalog, _root));
    }

    [Fact]
    public void BodyOf_StopsScanningHeaders_AtTheFirstLineThatIsNotOne()
    {
        var path = IoPath.Combine(_root, "prose.adp");
        File.WriteAllText(path, Mindmap.Origin.MimeType + "\nthis is not a header\nbody: shared/model.mm\n");

        var body = DiagramFilePair.BodyOf(path, _catalog, _root);

        Assert.NotNull(body);
        Assert.True(body!.Value.IsOwned, "a body: line after prose must not be honoured");
    }

    [Fact]
    public void SiblingOf_ARegistrationNamingABodyItDoesNotOwn_IsNull()
    {
        // This is what stops a delete or a rename of one C4 view carrying off the shared model
        // that several other views also open.
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm");

        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public async Task Delete_ARegistrationNamingASharedBody_LeavesTheBodyAlone()
    {
        // The safety property the header introduces: two registrations over one document, and
        // deleting one must not destroy the model the other still opens.
        Directory.CreateDirectory(IoPath.Combine(_root, "shared"));
        var shared = IoPath.Combine(_root, "shared", "model.mm");
        File.WriteAllText(shared, "<map/>");
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm");

        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(adp));
        Assert.True(File.Exists(shared), "deleting one view destroyed the shared model");
    }

}
