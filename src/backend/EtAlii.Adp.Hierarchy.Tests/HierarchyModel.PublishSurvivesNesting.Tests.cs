using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// A publish over a nested registration's subject must not un-nest it. The product's writes go
/// through AdpFileWriter - a scratch file moved onto the destination - so what the watcher sees
/// is not a content change but whatever the swap raises, and the tree's shape has to survive it.
/// Found in the field: renaming an element inline flattened every .adp nested under the diagram.
/// </summary>
public class HierarchyModelPublishSurvivesNestingTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog([Mindmap]);
    private readonly List<string> _events = [];

    public HierarchyModelPublishSurvivesNestingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task APublishOntoTheSubject_LeavesItsRegistrationNested()
    {
        // Arrange: a subject with one nested registration, exactly as the explorer shows them.
        var subjectPath = IoPath.Combine(_root, "plan.mm");
        await File.WriteAllTextAsync(subjectPath, "<map/>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "plan.adp"), Mindmap.Origin.MimeType + "\n", TestContext.Current.CancellationToken);

        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null));
        Assert.Equal("plan.mm", subject.Name);
        Assert.Equal("plan.adp", Assert.Single(model.ListChildren(subject.Id)).Name);

        // What a connected client would hold: it can only follow the emitted events, so the
        // replayed tree HAS to land where a fresh listing lands. A model that heals itself
        // while emitting events no client can follow has still flattened every explorer.
        var replayedParents = new Dictionary<ShortGuid, ShortGuid?> { [subject.Id] = null };
        var replayedNames = new Dictionary<ShortGuid, string> { [subject.Id] = subject.Name };
        foreach (var child in model.ListChildren(subject.Id))
        {
            replayedParents[child.Id] = subject.Id;
            replayedNames[child.Id] = child.Name;
        }

        model.EntryChanged += change =>
        {
            lock (_events)
            {
                switch (change)
                {
                    case HierarchyEntryCreated created:
                        replayedParents[created.Entry.Id] = created.Entry.ParentId;
                        replayedNames[created.Entry.Id] = created.Entry.Name;
                        _events.Add($"emit Created {created.Entry.Name}");
                        break;
                    case HierarchyEntryRemoved removed:
                        replayedParents.Remove(removed.EntryId);
                        replayedNames.Remove(removed.EntryId);
                        _events.Add("emit Removed");
                        break;
                    case HierarchyEntryRenamed renamed:
                        replayedNames[renamed.EntryId] = renamed.NewName;
                        _events.Add($"emit Renamed->{renamed.NewName}");
                        break;
                    case HierarchyEntryUpdated { ParentChanged: true } moved:
                        replayedParents[moved.EntryId] = moved.ParentId;
                        _events.Add("emit Reparented");
                        break;
                }
            }
        };

        // The production pipeline, verbatim: the same watcher wrapper, feeding the same model.
        using var watcher = new RootFolderWatcher(
            _root,
            (changeType, oldPath, newPath) =>
            {
                lock (_events)
                {
                    _events.Add($"{changeType}: {IoPath.GetFileName(oldPath)} -> {IoPath.GetFileName(newPath)}");
                }

                model.OnWatcherEvent(changeType, oldPath, newPath);
            },
            _ => { });

        // Act: what every module's document store now does on save.
        AdpFileWriter.Save(subjectPath, "<map>renamed</map>");

        // The watcher raises on its own thread; give the swap's events time to land.
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Assert: the registration is still under its subject, and not at the root.
        string raw;
        lock (_events)
        {
            raw = string.Join(" | ", _events);
        }

        await File.WriteAllTextAsync(IoPath.Combine(IoPath.GetTempPath(), "adp-nest-events.txt"), raw, TestContext.Current.CancellationToken);

        var rootChildren = model.ListChildren(null);
        var flattened = rootChildren.Where(entry => entry.Name == "plan.adp").ToList();
        Assert.True(flattened.Count == 0, $"plan.adp surfaced at the root - the publish un-nested it. Watcher saw: {raw}");

        var stillKnown = rootChildren.SingleOrDefault(entry => entry.Name == "plan.mm");
        Assert.True(stillKnown is not null, $"plan.mm vanished from the root. Watcher saw: {raw}");
        var nested = model.ListChildren(stillKnown.Id);
        Assert.True(nested.Any(entry => entry.Name == "plan.adp"), $"plan.adp is no longer under plan.mm. Watcher saw: {raw}");

        // And the replayed client tree agrees: plan.adp's parent is an entry named plan.mm.
        ShortGuid? replayedParent;
        lock (_events)
        {
            var registrationPair = replayedNames.Single(pair => pair.Value == "plan.adp");
            replayedParent = replayedParents[registrationPair.Key];
        }

        var parentName = replayedParent is { } parentId && replayedNames.TryGetValue(parentId, out var name) ? name : "<the root>";
        Assert.True(parentName == "plan.mm", $"a client following the events holds plan.adp under {parentName}. Watcher saw: {raw}");
    }
}
