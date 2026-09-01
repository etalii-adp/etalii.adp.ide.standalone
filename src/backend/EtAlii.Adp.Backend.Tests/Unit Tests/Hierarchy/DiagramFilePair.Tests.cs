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
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
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
        // Act.
        var adp = WriteRegistration("domain", Mindmap);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "domain.mm"), DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_ARegistrationWhoseTypeKeepsNoBody_IsNull()
    {
        // Act.
        var adp = WriteRegistration("classes", ClassDiagram);

        // Assert.
        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_AnUnknownMimeType_IsNull()
    {
        // Arrange and act.
        var adp = IoPath.Combine(_root, "mystery.adp");
        File.WriteAllText(adp, "nobody/knows\n");

        // Assert.
        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingOf_APlainFile_IsNull()
    {
        // Arrange and act.
        var txt = IoPath.Combine(_root, "notes.txt");
        File.WriteAllText(txt, "freeplane/mindmap\n"); // the content is not what makes a registration file

        // Assert.
        Assert.Null(DiagramFilePair.SiblingOf(txt, _catalog));
    }

    // ---- create: both or neither -----------------------------------------------------

    [Fact]
    public async Task Create_WithASibling_WritesBothFiles()
    {
        // Arrange.
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");

        // Act.
        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("freeplane/mindmap\n", await File.ReadAllTextAsync(IoPath.Combine(_root, "domain.adp"), TestContext.Current.CancellationToken));
        Assert.Equal("<map/>", await File.ReadAllTextAsync(IoPath.Combine(_root, "domain.mm"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_WhenTheSiblingNameIsTaken_LeavesNeitherBehind()
    {
        // Arrange.
        // The registration file would have been created fine; the sibling's name is what
        // collides. A half-created pair must never be observable (Requirement 1.6).
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.mm"), "someone else's map", TestContext.Current.CancellationToken);
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");

        // Act.
        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp")), "the registration file was left behind");
        Assert.Equal("someone else's map", await File.ReadAllTextAsync(IoPath.Combine(_root, "domain.mm"), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_root, "~adp-*"));
    }

    [Fact]
    public async Task Create_WithASibling_UndoRemovesBoth()
    {
        // Arrange.
        var command = new CreateDiagramFileCommand(_root, "domain.adp", "freeplane/mindmap", "domain.mm", "<map/>");
        await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")));
    }

    [Fact]
    public async Task Create_WithoutASibling_BehavesExactlyAsBefore()
    {
        // Arrange.
        var command = new CreateDiagramFileCommand(_root, "classes.adp", "uml/class");

        // Act.
        var result = await _history.ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(Directory.GetFiles(_root));
    }

    // ---- rename: the pair stays a pair ---------------------------------------------

    [Fact]
    public async Task Rename_ARegistrationFile_RenamesItsSiblingWithIt()
    {
        // Arrange.
        var adp = WriteRegistration("domain", Mindmap);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.mm"), "<map/>", TestContext.Current.CancellationToken);

        // Act.
        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.adp")));
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.mm")));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")));
    }

    [Fact]
    public async Task Rename_ARegistrationFile_UndoRestoresBothNames()
    {
        // Arrange.
        var adp = WriteRegistration("domain", Mindmap);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.mm"), "<map/>", TestContext.Current.CancellationToken);
        await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "domain.adp")));
        Assert.True(File.Exists(IoPath.Combine(_root, "domain.mm")));
        Assert.False(File.Exists(IoPath.Combine(_root, "renamed.mm")));
    }

    [Fact]
    public async Task Rename_WhenTheSiblingsNewNameIsTaken_ChangesNothing()
    {
        // Arrange.
        var adp = WriteRegistration("domain", Mindmap);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.mm"), "<map/>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "renamed.mm"), "in the way", TestContext.Current.CancellationToken);

        // Act.
        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("renamed.mm", result.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(adp), "the registration file was moved although its sibling could not follow");
        Assert.Equal("in the way", await File.ReadAllTextAsync(IoPath.Combine(_root, "renamed.mm"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rename_ARegistrationWhoseBodyIsMissing_RenamesTheRegistrationAlone()
    {
        // Arrange.
        // Requirement 2.12: a missing body is a recoverable state, not a broken diagram.
        var adp = WriteRegistration("domain", Mindmap);

        // Act.
        var result = await _history.ExecuteAsync(new RenameEntryCommand(adp, "renamed.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "renamed.adp")));
    }

    // ---- delete: the pair goes together --------------------------------------------

    [Fact]
    public async Task Delete_ARegistrationFile_DeletesItsSiblingWithIt()
    {
        // Arrange.
        var adp = WriteRegistration("domain", Mindmap);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "domain.mm"), "<map/>", TestContext.Current.CancellationToken);

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(adp));
        Assert.False(File.Exists(IoPath.Combine(_root, "domain.mm")), "the body was orphaned");
    }

    [Fact]
    public async Task Delete_ARegistrationWithNoBody_DeletesJustTheRegistration()
    {
        // Arrange.
        var adp = WriteRegistration("classes", ClassDiagram);

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        // Assert.
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
        // Arrange.
        // The case every existing type is in, which must behave exactly as it always has.
        var adp = WriteRegistration("domain", Mindmap);

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert.
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "domain.mm"), body.Value.Path);
        Assert.True(body.Value.IsOwned);
        Assert.Null(body.Value.ViewKey);
    }

    [Fact]
    public void BodyOf_ABodyHeader_ResolvesAgainstTheProjectRoot_AndIsNotOwned()
    {
        // Arrange.
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm", "containers");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert.
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "shared", "model.mm"), body.Value.Path);
        Assert.Equal("containers", body.Value.ViewKey);
        Assert.False(body.Value.IsOwned);
    }

    [Fact]
    public void BodyOf_AViewHeaderWithoutABody_StillNamesTheView_AndStaysOwned()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "solo.adp");
        File.WriteAllText(path, Mindmap.Origin.MimeType + "\nview: context\n");

        // Act.
        var body = DiagramFilePair.BodyOf(path, _catalog, _root);

        // Assert.
        Assert.NotNull(body);
        Assert.Equal("context", body.Value.ViewKey);
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
        // Arrange.
        var path = IoPath.Combine(_root, "prose.adp");
        File.WriteAllText(path, Mindmap.Origin.MimeType + "\nthis is not a header\nbody: shared/model.mm\n");

        // Act.
        var body = DiagramFilePair.BodyOf(path, _catalog, _root);

        // Assert.
        Assert.NotNull(body);
        Assert.True(body.Value.IsOwned, "a body: line after prose must not be honoured");
    }

    [Fact]
    public void SiblingOf_ARegistrationNamingABodyItDoesNotOwn_IsNull()
    {
        // Act.
        // This is what stops a delete or a rename of one C4 view carrying off the shared model
        // that several other views also open.
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm");

        // Assert.
        Assert.Null(DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public async Task Delete_ARegistrationNamingASharedBody_LeavesTheBodyAlone()
    {
        // Arrange.
        // The safety property the header introduces: two registrations over one document, and
        // deleting one must not destroy the model the other still opens.
        Directory.CreateDirectory(IoPath.Combine(_root, "shared"));
        var shared = IoPath.Combine(_root, "shared", "model.mm");
        await File.WriteAllTextAsync(shared, "<map/>", TestContext.Current.CancellationToken);
        var adp = WriteRegistrationNamingBody("containers", Mindmap, "shared/model.mm");

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(adp), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(adp));
        Assert.True(File.Exists(shared), "deleting one view destroyed the shared model");
    }

    /// <summary>
    /// Requirement 4.3, corrected: .NET does NOT apply the Unix "leading dot means no extension"
    /// convention. The requirement originally asserted the opposite of both values below, and the
    /// error survived until its own "verify by test" clause forced a measurement. These two facts
    /// pin the measured behaviour so the correction cannot decay back into folklore - name-based
    /// reasoning about a bare `.adp` is the hazard (the base name is empty), extension-based
    /// reasoning is safe.
    /// </summary>
    [Fact]
    public void GetExtension_OfABareFolderRegistrationName_IsTheWholeName()
    {
        // Arrange, act and assert.
        Assert.Equal(DiagramFileName.Extension, IoPath.GetExtension(DiagramFileName.Extension));
    }

    [Fact]
    public void GetFileNameWithoutExtension_OfABareFolderRegistrationName_IsEmpty()
    {
        // Arrange, act and assert.
        Assert.Equal("", IoPath.GetFileNameWithoutExtension(DiagramFileName.Extension));
    }

    [Fact]
    public void StripExtension_OfABareFolderRegistrationName_IsEmpty()
    {
        // Arrange, act and assert: the empty base name is exactly why Requirement 4.4 excludes
        // the folder-scoped form from sibling derivation - a derived path would be bare extension.
        Assert.Equal("", DiagramFileName.StripExtension(DiagramFileName.Extension));
    }

    /// <summary>
    /// Requirement 2.4 and 6.2: ownership is per set, not per pair. A qualified registration
    /// derives the shared subject to OPEN it, but never owns it - only the unqualified reading,
    /// where the name is the subject, keeps the classic take-the-sibling-along behaviour.
    /// </summary>
    [Fact]
    public void SiblingOf_AQualifiedRegistration_DoesNotOwnTheSharedSubject()
    {
        // Arrange: two qualified registrations over one subject.
        File.WriteAllText(IoPath.Combine(_root, "test.mm"), "<map/>");
        var first = WriteRegistration("test.first", Mindmap);
        WriteRegistration("test.second", Mindmap);

        // Act and assert: it opens the subject but may not carry it.
        Assert.Null(DiagramFilePair.SiblingOf(first, _catalog));
        var body = DiagramFilePair.BodyOf(first, _catalog, _root);
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "test.mm"), body.Value.Path);
        Assert.False(body.Value.IsOwned);
    }

    [Fact]
    public async Task Delete_OneOfSeveralQualifiedRegistrations_LeavesTheSubjectAndItsPeers()
    {
        // Arrange.
        var subject = IoPath.Combine(_root, "test.mm");
        await File.WriteAllTextAsync(subject, "<map/>", TestContext.Current.CancellationToken);
        var first = WriteRegistration("test.first", Mindmap);
        var second = WriteRegistration("test.second", Mindmap);

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(first), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(subject), "deleting one registration carried off the shared subject");
        Assert.True(File.Exists(second), "deleting one registration destroyed a peer registration");
    }

    /// <summary>
    /// Requirement 8.4: a registration naming a MIME type the catalog does not carry resolves to
    /// no definition and no body, and that is NOT an error. No example can exercise this branch -
    /// all 39 tracked registrations name known types, measured across every one - so the fixture
    /// is created, not found.
    /// </summary>
    [Fact]
    public void ARegistrationNamingAnUnknownType_ResolvesToNoDefinitionAndNoBody()
    {
        // Arrange.
        var fixture = IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "unknown-type.adp");
        Assert.True(File.Exists(fixture), "the unknown-type fixture did not ship with the tests");

        // Act.
        var definition = DiagramFilePair.DefinitionOf(fixture, _catalog);
        var body = DiagramFilePair.BodyOf(fixture, _catalog, IoPath.GetDirectoryName(fixture));

        // Assert: unknown, bodyless, and quietly so.
        Assert.True(DiagramFilePair.IsRegistrationFile(fixture));
        Assert.Null(definition);
        Assert.Null(body);
    }

    /// <summary>
    /// The branch `code-level.adp` actually exercises, distinct from the unknown-type one above:
    /// the definition IS found, and it is `HasDocumentSibling` - no declared extension - that
    /// yields no body. A test asserting only "no body" would pass for both branches and prove
    /// nothing about which ran; these assertions tell them apart. (Whether this case deserves a
    /// requirement of its own is the reviewer's open question, deliberately not answered here.)
    /// </summary>
    [Fact]
    public void ARegistrationOfAKnownTypeKeepingNoBody_ResolvesItsDefinitionButNoBody()
    {
        // Arrange: ClassDiagram declares no Extension, exactly like the shipped c4/code.
        var adp = WriteRegistration("code-level", ClassDiagram);

        // Act.
        var definition = DiagramFilePair.DefinitionOf(adp, _catalog);
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert: found, and bodyless for a stated reason rather than an unknown one.
        Assert.NotNull(definition);
        Assert.False(definition.HasDocumentSibling);
        Assert.Null(body);
    }

    [Fact]
    public void SiblingOf_AnUnqualifiedRegistration_StillOwnsItsSibling()
    {
        // Arrange: the classic pair, exactly as it has always behaved (Requirement 11.1).
        File.WriteAllText(IoPath.Combine(_root, "solo.mm"), "<map/>");
        var adp = WriteRegistration("solo", Mindmap);

        // Act and assert.
        Assert.Equal(IoPath.Combine(_root, "solo.mm"), DiagramFilePair.SiblingOf(adp, _catalog));
    }

    [Fact]
    public void SiblingDerivation_OfAFolderScopedRegistration_ProducesNoPath()
    {
        // Arrange.
        var folderScoped = IoPath.Combine(_root, DiagramFileName.Extension);

        // Act.
        var derived = DiagramFilePair.SiblingPathFor(folderScoped, Mindmap.Extension);

        // Assert: empty rather than a path that is just the extension (Requirement 4.4).
        Assert.Equal("", derived);
    }

}
