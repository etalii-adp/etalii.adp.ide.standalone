using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Editor;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Report D10: the model raises <see cref="HierarchyModel.EntryChanged"/> while it holds its own
/// lock, so a subscriber that waits on anything else asking the model a question deadlocks the
/// two. Each case drives one raising site, and its subscriber asks the model a question from
/// another thread for each change that site raises; a change raised under the lock leaves that
/// question unanswered until the patience runs out.
/// </summary>
public class HierarchyModelEventsOutsideTheLockTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new ToolOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly DiagramDefinition Structure = new(new ToolOrigin("ansible", "structure"), "Ansible structure", Subject: DiagramSubject.Folder);
    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);

    // Far longer than an uncontended lock takes on a loaded runner, so a timeout is the lock.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap, Structure);

    public HierarchyModelEventsOutsideTheLockTests()
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

    public static TheoryData<string> RaisingSites =>
    [
        "OnCreated",
        "OnRemoved",
        "OnRemoved, a subject's registration back to its folder",
        "RecomputeHasChildren",
        "OnRenamed",
        "ApplyLocalRename",
        "SyncFolder, created on a reconcile",
        "SyncFolder, removed on a reconcile",
        "ApplyNesting, a registration under its subject",
        "ApplyNesting, a subject's children",
        "RefreshDiagramStates, over the watch",
        "RefreshDiagramStates, a folder's own state on a listing",
    ];

    [Theory]
    [MemberData(nameof(RaisingSites))]
    public void EntryChanged_IsRaisedOutsideTheLock_SoASubscriberCanAskTheModelFromAnotherThread(string site)
    {
        // Arrange.
        var model = new HierarchyModel(_root, _catalog, new DiagramFileRouter(_catalog), new EditorResolver(new StubEditorCatalog(Plain)));
        (Action act, Predicate<HierarchyEntryChange> raisedBySite) = Arrange(site, model);
        var heard = new List<HierarchyEntryChange>();
        var raisedUnderTheLock = new List<HierarchyEntryChange>();
        model.EntryChanged += change =>
        {
            heard.Add(change);
            if (raisedBySite(change) && raisedUnderTheLock.Count == 0 && !AnswersFromAnotherThread(model))
            {
                raisedUnderTheLock.Add(change);
            }
        };

        // Act.
        act();

        // Assert: the site raised its change, and not with the lock held.
        Assert.Contains(heard, raisedBySite);
        Assert.True(raisedUnderTheLock.Count == 0, $"{site}: {string.Join(", ", raisedUnderTheLock)} was raised while the model held its lock; a question from another thread waited {Patience.TotalSeconds} s for it");
    }

    // A question that needs the model's lock and nothing else, asked from a thread of its own.
    private static bool AnswersFromAnotherThread(HierarchyModel model)
    {
        var asking = new Thread(() => model.TryResolvePath(ShortGuid.NewShortGuid(), out _, out _)) { IsBackground = true };
        asking.Start();
        return asking.Join(Patience);
    }

    private (Action Act, Predicate<HierarchyEntryChange> RaisedBySite) Arrange(string site, HierarchyModel model)
    {
        switch (site)
        {
            case "OnCreated":
            {
                model.ListChildren(null);
                var created = Write("notes.txt", "notes\n");
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Created, null, created), change => change is HierarchyEntryCreated);
            }
            case "OnRemoved":
            {
                var removed = Write("notes.txt", "notes\n");
                model.ListChildren(null);
                File.Delete(removed);
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Deleted, removed, null), change => change is HierarchyEntryRemoved);
            }
            case "OnRemoved, a subject's registration back to its folder":
            {
                var subject = Write("ideas.mm", "<map/>");
                Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
                model.ListChildren(null);
                File.Delete(subject);
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Deleted, subject, null), change => change is HierarchyEntryUpdated { ParentChanged: true, ParentId: null });
            }
            case "RecomputeHasChildren":
            {
                Directory.CreateDirectory(IoPath.Combine(_root, "empty"));
                var folder = Assert.Single(model.ListChildren(null));
                var created = Write(IoPath.Combine("empty", "notes.txt"), "notes\n");
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Created, null, created), change => change is HierarchyEntryUpdated { HasChildren: true } updated && updated.EntryId == folder.Id);
            }
            case "OnRenamed":
            {
                var oldPath = Write("notes.txt", "notes\n");
                model.ListChildren(null);
                var newPath = IoPath.Combine(_root, "renamed.txt");
                File.Move(oldPath, newPath);
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Renamed, oldPath, newPath), change => change is HierarchyEntryRenamed);
            }
            case "ApplyLocalRename":
            {
                var oldPath = Write("notes.txt", "notes\n");
                model.ListChildren(null);
                var newPath = IoPath.Combine(_root, "renamed.txt");
                File.Move(oldPath, newPath);
                return (() => model.ApplyLocalRename(oldPath, newPath), change => change is HierarchyEntryRenamed);
            }
            case "SyncFolder, created on a reconcile":
            {
                model.ListChildren(null);
                Write("notes.txt", "notes\n");
                return (model.Reconcile, change => change is HierarchyEntryCreated);
            }
            case "SyncFolder, removed on a reconcile":
            {
                var removed = Write("notes.txt", "notes\n");
                model.ListChildren(null);
                File.Delete(removed);
                return (model.Reconcile, change => change is HierarchyEntryRemoved);
            }
            case "ApplyNesting, a registration under its subject":
            {
                Write("ideas.mm", "<map/>");
                var subject = Assert.Single(model.ListChildren(null));
                var registration = Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration), change => change is HierarchyEntryUpdated { ParentChanged: true } updated && updated.ParentId == subject.Id);
            }
            case "ApplyNesting, a subject's children":
            {
                Write("ideas.mm", "<map/>");
                var subject = Assert.Single(model.ListChildren(null));
                var registration = Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration), change => change is HierarchyEntryUpdated { HasChildren: true, ParentChanged: false } updated && updated.EntryId == subject.Id);
            }
            case "RefreshDiagramStates, over the watch":
            {
                Write("ideas.mm", "<map/>");
                var subject = Assert.Single(model.ListChildren(null));
                var registration = Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
                return (() => model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration), change => change is HierarchyEntryUpdated { DiagramState: EntryDiagramState.Registered } updated && updated.EntryId == subject.Id);
            }
            case "RefreshDiagramStates, a folder's own state on a listing":
            {
                Directory.CreateDirectory(IoPath.Combine(_root, "infrastructure"));
                var folder = Assert.Single(model.ListChildren(null));
                Write(IoPath.Combine("infrastructure", "structure.adp"), Structure.Origin.MimeType + "\n");
                return (() => model.ListChildren(folder.Id), change => change is HierarchyEntryUpdated { DiagramState: EntryDiagramState.Registered } updated && updated.EntryId == folder.Id);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(site), site, "No such raising site");
        }
    }

    private string Write(string relativePath, string content)
    {
        var path = IoPath.Combine(_root, relativePath);
        File.WriteAllText(path, content);
        return path;
    }
}
