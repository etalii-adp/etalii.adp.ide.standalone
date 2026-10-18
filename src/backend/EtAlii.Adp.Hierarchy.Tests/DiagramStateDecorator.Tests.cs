using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Editor;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// <see cref="DiagramStateDecorator"/> on its own: what it decides for a folder's files, the folder
/// itself and the folders in it, and that only the folder's own decision is raised on a listing.
/// </summary>
public class DiagramStateDecoratorTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new ToolOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly DiagramDefinition Structure = new(new ToolOrigin("ansible", "structure"), "Ansible structure", Subject: DiagramSubject.Folder);
    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap, Structure);

    public DiagramStateDecoratorTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private sealed class StubEditorCatalog(params EditorDefinition[] definitions) : IEditorDefinitionCatalog
    {
        public IReadOnlyList<EditorDefinition> All { get; } = definitions;
    }

    private static EntryNode Entry(string name, bool isFolder = false) =>
        new(ShortGuid.NewShortGuid(), null, name, isFolder, Available: true, HasChildren: false);

    [Fact]
    public void Decide_AFoldersFilesAndTheFoldersInIt_AndRaisesOnlyTheFoldersOwnOnAListing()
    {
        // Arrange: in "docs", a registered pair, a bare body, a text file and a folder holding a
        // folder-subject registration.
        var docs = IoPath.Combine(_root, "docs");
        Directory.CreateDirectory(IoPath.Combine(docs, "infrastructure"));
        File.WriteAllText(IoPath.Combine(docs, "ideas.mm"), "<map/>");
        File.WriteAllText(IoPath.Combine(docs, "ideas.adp"), Mindmap.Origin.MimeType + "\n");
        File.WriteAllText(IoPath.Combine(docs, "bare.mm"), "<map/>");
        File.WriteAllText(IoPath.Combine(docs, "notes.txt"), "notes\n");
        File.WriteAllText(IoPath.Combine(docs, "infrastructure", "structure.adp"), Structure.Origin.MimeType + "\n");
        var folder = Entry("docs", isFolder: true);
        var contained = new[] { Entry("ideas.mm"), Entry("ideas.adp"), Entry("bare.mm"), Entry("notes.txt"), Entry("infrastructure", isFolder: true) };
        var decorator = new DiagramStateDecorator(
            _root,
            new DiagramFileRouter(_catalog),
            new EditorResolver(new StubEditorCatalog(Plain)),
            (folderPath, name) => name.Equals("ideas.adp", StringComparison.OrdinalIgnoreCase) && folderPath == docs ? "ideas.mm" : null);

        // Act.
        var decisions = decorator.Decide(folder, docs, contained).ToDictionary(decision => decision.EntryId);

        // Assert.
        var byName = contained.Append(folder).ToDictionary(entry => entry.Name, entry => decisions[entry.Id]);
        Assert.Equal(EntryDiagramState.Registered, byName["ideas.mm"].State);
        Assert.Equal(EntryDiagramState.Registered, byName["ideas.adp"].State);
        Assert.Equal(EntryDiagramState.Potential, byName["bare.mm"].State);
        Assert.Equal(EntryDiagramState.Unspecified, byName["notes.txt"].State);
        Assert.Equal(EntryDiagramState.Registered, byName["infrastructure"].State);
        Assert.Equal(["docs"], byName.Where(pair => pair.Value.RaisedOnAListing).Select(pair => pair.Key));
    }
}
