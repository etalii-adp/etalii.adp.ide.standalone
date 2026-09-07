using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The hierarchy with nesting applied (adp-file-nesting Requirements 3, 8 and 10): a
/// registration is a child of its subject file, in listings and over the watch alike.
/// </summary>
public class HierarchyModelNestingTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new TestDiagramDefinitionCatalog(Mindmap);

    public HierarchyModelNestingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CreateRegistration(string baseName)
    {
        var path = IoPath.Combine(_root, baseName + ".adp");
        File.WriteAllText(path, Mindmap.Origin.MimeType + "\n");
        return path;
    }

    private string CreateSubject(string name)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "<map/>");
        return path;
    }

    [Fact]
    public void ListChildren_NestsARegistrationUnderItsSubject_AndNowhereElse()
    {
        // Arrange.
        CreateSubject("test.mm");
        CreateRegistration("test");
        var model = new HierarchyModel(_root, _catalog);

        // Act.
        var rootChildren = model.ListChildren(null);

        // Assert: the folder listing carries the subject only (Requirement 3.5)...
        var subject = Assert.Single(rootChildren);
        Assert.Equal("test.mm", subject.Name);
        Assert.True(subject.HasChildren, "a subject with a registration must be expandable");

        // ...and the FILE listing carries the registration, parented to it (Requirement 3.2).
        var nested = Assert.Single(model.ListChildren(subject.Id));
        Assert.Equal("test.adp", nested.Name);
        Assert.Equal(subject.Id, nested.ParentId);
    }

    [Fact]
    public void ListChildren_OfASubject_OrdersTheUnqualifiedRegistrationFirst()
    {
        // Arrange.
        CreateSubject("test.mm");
        CreateRegistration("test.second");
        CreateRegistration("test");
        CreateRegistration("test.first");
        foreach (var qualified in new[] { "test.second", "test.first" })
        {
            // A qualified registration ADP authors carries its body as a header (Requirement 2.3).
            File.WriteAllText(IoPath.Combine(_root, qualified + ".adp"), Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        }

        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null), c => c.Name == "test.mm");

        // Act.
        var nested = model.ListChildren(subject.Id);

        // Assert: unqualified first - it is the activation default - then qualifiers ordinal.
        Assert.Equal(new[] { "test.adp", "test.first.adp", "test.second.adp" }, nested.Select(n => n.Name));
    }

    [Fact]
    public void OnWatcherEvent_CreatingARegistration_NestsItWithoutAReload()
    {
        // Arrange.
        CreateSubject("test.mm");
        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null));
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        var registration = CreateRegistration("test");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration);

        // Assert: the create arrives, the re-parent update follows, and the subject's
        // has_children flips - a client applying these in order converges (Requirement 10.4).
        var created = changes.OfType<HierarchyEntryCreated>().Single();
        Assert.Equal("test.adp", created.Entry.Name);
        var reparent = changes.OfType<HierarchyEntryUpdated>().Single(u => u.ParentChanged);
        Assert.Equal(created.Entry.Id, reparent.EntryId);
        Assert.Equal(subject.Id, reparent.ParentId);
        var expander = changes.OfType<HierarchyEntryUpdated>().Single(u => !u.ParentChanged && u.EntryId == subject.Id);
        Assert.True(expander.HasChildren);
    }

    [Fact]
    public void OnWatcherEvent_CreatingAMissingSubject_ReparentsItsOrphan()
    {
        // Arrange: the registration exists first, its subject does not (Requirement 8.1) -
        // it sits at the level it sits on disk.
        CreateRegistration("test");
        var model = new HierarchyModel(_root, _catalog);
        var orphan = Assert.Single(model.ListChildren(null));
        Assert.Equal("test.adp", orphan.Name);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act: the subject appears (Requirement 8.3, 10.3).
        var subjectPath = CreateSubject("test.mm");
        model.OnWatcherEvent(WatcherChangeTypes.Created, null, subjectPath);

        // Assert: the orphan re-parents as an UPDATE - same id, no remove/create pair.
        var subjectCreated = changes.OfType<HierarchyEntryCreated>().Single();
        var reparent = changes.OfType<HierarchyEntryUpdated>().Single(u => u.ParentChanged);
        Assert.Equal(orphan.Id, reparent.EntryId);
        Assert.Equal(subjectCreated.Entry.Id, reparent.ParentId);
        Assert.Empty(changes.OfType<HierarchyEntryRemoved>());
    }

    [Fact]
    public void OnWatcherEvent_RemovingTheSubject_OrphansItsRegistrationVisibly()
    {
        // Arrange.
        var subjectPath = CreateSubject("test.mm");
        CreateRegistration("test");
        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null));
        var nested = Assert.Single(model.ListChildren(subject.Id));
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        File.Delete(subjectPath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, subjectPath, null);

        // Assert: the registration survives its subject, back at the root, still listed -
        // a broken pair the user can see and fix rather than a vanished entry.
        var reparent = changes.OfType<HierarchyEntryUpdated>().Single(u => u.ParentChanged && u.EntryId == nested.Id);
        Assert.Null(reparent.ParentId);
        Assert.Contains(model.ListChildren(null), e => e.Id == nested.Id);
        Assert.DoesNotContain(changes.OfType<HierarchyEntryRemoved>(), r => r.EntryId == nested.Id);
    }

    [Fact]
    public void OnWatcherEvent_RemovingTheLastRegistration_CollapsesTheSubject()
    {
        // Arrange.
        CreateSubject("test.mm");
        var registrationPath = CreateRegistration("test");
        var model = new HierarchyModel(_root, _catalog);
        var subject = Assert.Single(model.ListChildren(null));
        model.ListChildren(subject.Id);
        var changes = new List<HierarchyEntryChange>();
        model.EntryChanged += changes.Add;

        // Act.
        File.Delete(registrationPath);
        model.OnWatcherEvent(WatcherChangeTypes.Deleted, registrationPath, null);

        // Assert: the subject's has_children is pushed as false (Requirement 10.2).
        var collapsed = changes.OfType<HierarchyEntryUpdated>().Single(u => u.EntryId == subject.Id);
        Assert.False(collapsed.HasChildren);
    }

    [Fact]
    public void WithoutACatalog_NothingNests_AndTheOldShapeHolds()
    {
        // Arrange: the model without diagram knowledge is the pre-nesting model exactly.
        CreateSubject("test.mm");
        CreateRegistration("test");
        var model = new HierarchyModel(_root);

        // Act.
        var children = model.ListChildren(null);

        // Assert.
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.Null(c.ParentId));
    }
}
