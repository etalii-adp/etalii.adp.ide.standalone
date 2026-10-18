using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// <see cref="HierarchyIndex"/> on its own: an entry is found by its parent and by the folder it
/// lives in, which differ for a nested registration, and both follow a re-parent, a removal and a
/// folder's move.
/// </summary>
public class HierarchyIndexTests
{
    private static readonly string Root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", "index");

    private static EntryNode Entry(string name, ShortGuid? parentId = null, bool isFolder = false) =>
        new(ShortGuid.NewShortGuid(), parentId, name, isFolder, Available: true, HasChildren: false);

    [Fact]
    public void ARegistrationNestedUnderItsSubject_IsItsSubjectsChild_ButLivesInTheFolder()
    {
        // Arrange.
        var index = new HierarchyIndex();
        var folder = Entry("docs", isFolder: true);
        var subject = Entry("ideas.mm", folder.Id);
        var registration = Entry("ideas.adp", folder.Id);
        index.Add(folder, IoPath.Combine(Root, "docs"));
        index.Add(subject, IoPath.Combine(Root, "docs", "ideas.mm"));
        index.Add(registration, IoPath.Combine(Root, "docs", "ideas.adp"));

        // Act.
        index.Set(registration with { ParentId = subject.Id });

        // Assert.
        Assert.Equal([folder.Id], index.ChildrenOf(null).Select(e => e.Id));
        Assert.Equal([subject.Id], index.ChildrenOf(folder.Id).Select(e => e.Id));
        Assert.Equal([registration.Id], index.ChildrenOf(subject.Id).Select(e => e.Id));
        Assert.True(index.HasChildren(subject.Id));
        Assert.Equal(new[] { subject.Id, registration.Id }.Order(), index.In(IoPath.Combine(Root, "docs")).Select(e => e.Id).Order());
    }

    [Fact]
    public void RemoveSubtree_RemovesEverythingUnderTheEntry_FromEveryLookup()
    {
        // Arrange.
        var index = new HierarchyIndex();
        var folder = Entry("docs", isFolder: true);
        var subject = Entry("ideas.mm", folder.Id);
        var registration = Entry("ideas.adp", subject.Id);
        index.Add(folder, IoPath.Combine(Root, "docs"));
        index.Add(subject, IoPath.Combine(Root, "docs", "ideas.mm"));
        index.Add(registration, IoPath.Combine(Root, "docs", "ideas.adp"));

        // Act.
        var removed = index.RemoveSubtree(folder.Id);

        // Assert.
        Assert.Equal(new[] { folder.Id, subject.Id, registration.Id }.Order(), removed.Order());
        Assert.Empty(index.ChildrenOf(null));
        Assert.Empty(index.In(IoPath.Combine(Root, "docs")));
        Assert.False(index.ContainsPath(IoPath.Combine(Root, "docs", "ideas.adp")));
        Assert.False(index.Contains(registration.Id));
    }

    [Fact]
    public void Move_AFolder_CarriesEveryEntryLivingInIt_AndAFile_CarriesNone()
    {
        // Arrange.
        var index = new HierarchyIndex();
        var folder = Entry("before", isFolder: true);
        var subject = Entry("ideas.mm", folder.Id);
        var registration = Entry("ideas.adp", subject.Id);
        index.Add(folder, IoPath.Combine(Root, "before"));
        index.Add(subject, IoPath.Combine(Root, "before", "ideas.mm"));
        index.Add(registration, IoPath.Combine(Root, "before", "ideas.adp"));

        // Act.
        index.Move(folder.Id, IoPath.Combine(Root, "after"));
        index.Move(subject.Id, IoPath.Combine(Root, "after", "renamed.mm"));

        // Assert.
        Assert.True(index.TryGetPath(registration.Id, out var registrationPath));
        Assert.Equal(IoPath.Combine(Root, "after", "ideas.adp"), registrationPath);
        Assert.True(index.TryGetId(IoPath.Combine(Root, "after", "renamed.mm"), out var subjectId));
        Assert.Equal(subject.Id, subjectId);
        Assert.Empty(index.In(IoPath.Combine(Root, "before")));
        Assert.Equal(2, index.In(IoPath.Combine(Root, "after")).Count());
    }
}
