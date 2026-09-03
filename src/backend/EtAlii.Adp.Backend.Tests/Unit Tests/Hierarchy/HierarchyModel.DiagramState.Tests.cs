using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Editor;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The explorer's diagram state, computed by the model's own scan and kept live over the
/// watch (small-refinements Requirements 3.1, 3.2, 3.6): what each entry reports after a
/// listing, and that an <c>.adp</c> appearing or vanishing moves its subject's state on the
/// stream the tree already lives on.
/// </summary>
public class HierarchyModelDiagramStateTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly DiagramDefinition Pipeline = new(new DiagramOrigin("azure-devops", "pipeline"), "Azure DevOps pipeline", Extension: ".yml", SharedExtension: true);
    private static readonly DiagramDefinition Structure = new(new DiagramOrigin("ansible", "structure"), "Ansible structure", Subject: DiagramSubject.Folder);
    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);
    private static readonly EditorDefinition Markdown = new("markdown", "Markdown", Extensions: [".md"]);

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap, Pipeline, Structure);

    public HierarchyModelDiagramStateTests()
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

    private HierarchyModel Model() =>
        new(_root, _catalog, new DiagramFileRouter(_catalog), new EditorResolver(new StubEditorCatalog(Plain, Markdown)));

    private string Write(string relativePath, string content)
    {
        var path = IoPath.Combine(_root, relativePath);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ListChildren_ReportsEachEntrysState()
    {
        // Arrange: one of everything the rules distinguish.
        Write("ideas.mm", "<map/>");
        Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
        Write("bare.mm", "<map/>");
        Write("build.yml", "stages: []\n");
        Write("readme.md", "# Read me\n");
        Write("notes.txt", "notes\n");

        // Act.
        var model = Model();
        var byName = model.ListChildren(null).ToDictionary(entry => entry.Name);

        // Assert: a registered pair, a bare body, a registrable-but-unregistered file, an
        // editor-claimed file, and a file only the fallback answers.
        Assert.Equal(EntryDiagramState.Registered, byName["ideas.mm"].DiagramState);
        Assert.Equal(EntryDiagramState.Potential, byName["bare.mm"].DiagramState);
        Assert.Equal(EntryDiagramState.Potential, byName["build.yml"].DiagramState);
        Assert.Equal(EntryDiagramState.Potential, byName["readme.md"].DiagramState);
        Assert.Equal(EntryDiagramState.Unspecified, byName["notes.txt"].DiagramState);

        // The registration itself, nested under its subject, is Registered too.
        var nested = Assert.Single(model.ListChildren(byName["ideas.mm"].Id));
        Assert.Equal(EntryDiagramState.Registered, nested.DiagramState);
    }

    [Fact]
    public void ListChildren_AFolderHoldingAFolderSubjectRegistration_IsRegistered_OnceScanned()
    {
        // Arrange: the shipped shape - infrastructure/structure.adp registers its own folder.
        Directory.CreateDirectory(IoPath.Combine(_root, "infrastructure"));
        Write(IoPath.Combine("infrastructure", "structure.adp"), Structure.Origin.MimeType + "\n");
        var model = Model();
        var folder = Assert.Single(model.ListChildren(null));

        // Act: the folder's state comes from its contents, so its own scan decides it.
        model.ListChildren(folder.Id);

        // Assert.
        var rescanned = Assert.Single(model.ListChildren(null));
        Assert.Equal(EntryDiagramState.Registered, rescanned.DiagramState);
    }

    [Fact]
    public void OnWatcherEvent_AnAdpAppearingAndVanishing_MovesItsSubjectsState()
    {
        // Arrange: a bare body, listed, so the model is watching its folder.
        Write("bare.mm", "<map/>");
        var model = Model();
        var subject = Assert.Single(model.ListChildren(null));
        Assert.Equal(EntryDiagramState.Potential, subject.DiagramState);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act: the registration appears - Add as diagram…'s effect on disk.
        var registration = Write("bare.adp", Mindmap.Origin.MimeType + "\n");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration);

        // Assert: the subject's update rides the existing stream and carries the new state.
        var upgraded = changes.OfType<HierarchyEntryUpdated>().Last(update => update.EntryId == subject.Id);
        Assert.Equal(EntryDiagramState.Registered, upgraded.DiagramState);

        // Act: and the reverse (Requirement 3.6's other half).
        changes.Clear();
        File.Delete(registration);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, registration, null);

        // Assert.
        var downgraded = changes.OfType<HierarchyEntryUpdated>().Last(update => update.EntryId == subject.Id);
        Assert.Equal(EntryDiagramState.Potential, downgraded.DiagramState);
    }
}
