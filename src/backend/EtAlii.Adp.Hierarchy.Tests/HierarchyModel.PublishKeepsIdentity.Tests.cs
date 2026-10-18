using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// A publish over a file must not change the file's identity. The selection store tracks the
/// selected file by its entry id and clears the whole selection the moment that id stops
/// resolving - so if the swap AdpFileWriter performs makes the model retire the entry and mint
/// a new one, every property-grid commit deselects what was just edited. Found in the field:
/// changing a checkbox or a name in the property grid made the selection vanish.
/// </summary>
public class HierarchyModelPublishKeepsIdentityTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new ToolOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(500);

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap);

    public HierarchyModelPublishKeepsIdentityTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task APublishOntoTheSubject_KeepsItsEntryId()
    {
        // Arrange.
        var subjectPath = IoPath.Combine(_root, "plan.mm");
        await File.WriteAllTextAsync(subjectPath, "<map/>", TestContext.Current.CancellationToken);

        var model = new HierarchyModel(_root, TestToolRouters.For(_catalog));
        var subject = Assert.Single(model.ListChildren(null));
        var events = new List<string>();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var fileEvents = new ProjectFileEvents();
        using var watcher = fileEvents.Subscribe(
            _root,
            heard =>
            {
                lock (events)
                {
                    events.Add($"{heard.Kind}: {IoPath.GetFileName(heard.OldPath)} -> {IoPath.GetFileName(heard.Path)}");
                }

                model.OnFileEvent(heard);
                if (string.Equals(heard.Path, subjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    published.TrySetResult();
                }
            });

        // Act: the write every property-grid commit ends in.
        AdpFileWriter.Save(subjectPath, "<map>changed</map>");

        // Assert: the id selected before the publish still resolves, to the same file - which
        // is precisely the question HierarchyContextSourceResolver.Track asks before deciding
        // whether to clear the selection - once the publish is heard, and for as long as any
        // event the swap raised may still be landing.
        await published.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var lost = await Eventually.TrueAsync(() => !model.TryResolvePath(subject.Id, out _, out _), TestContext.Current.CancellationToken, Silence);
        string raw;
        lock (events)
        {
            raw = string.Join(" | ", events);
        }

        Assert.False(lost, $"the subject's entry id stopped resolving after a publish - the selection tracking it is cleared. Watcher saw: {raw}");
        var resolves = model.TryResolvePath(subject.Id, out var fullPath, out _);
        Assert.True(resolves, $"the subject's entry id no longer resolves after a publish - the selection tracking it is cleared. Watcher saw: {raw}");
        Assert.True(
            string.Equals(fullPath, subjectPath, StringComparison.OrdinalIgnoreCase),
            $"the subject's entry id resolves to {fullPath} rather than {subjectPath}. Watcher saw: {raw}");
    }
}
