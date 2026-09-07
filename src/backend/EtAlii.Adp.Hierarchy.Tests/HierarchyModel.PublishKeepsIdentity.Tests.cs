using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.TestSupport;
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
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog([Mindmap]);

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

        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null));
        var events = new List<string>();

        using var watcher = new RootFolderWatcher(
            _root,
            (changeType, oldPath, newPath) =>
            {
                lock (events)
                {
                    events.Add($"{changeType}: {IoPath.GetFileName(oldPath)} -> {IoPath.GetFileName(newPath)}");
                }

                model.OnWatcherEvent(changeType, oldPath, newPath);
            },
            _ => { });

        // Act: the write every property-grid commit ends in.
        AdpFileWriter.Save(subjectPath, "<map>changed</map>");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Assert: the id selected before the publish still resolves, to the same file - which
        // is precisely the question HierarchyContextSourceResolver.Track asks before deciding
        // whether to clear the selection.
        string raw;
        lock (events)
        {
            raw = string.Join(" | ", events);
        }

        var resolves = model.TryResolvePath(subject.Id, out var fullPath, out _);
        Assert.True(resolves, $"the subject's entry id no longer resolves after a publish - the selection tracking it is cleared. Watcher saw: {raw}");
        Assert.True(
            string.Equals(fullPath, subjectPath, StringComparison.OrdinalIgnoreCase),
            $"the subject's entry id resolves to {fullPath} rather than {subjectPath}. Watcher saw: {raw}");
    }
}
