using EtAlii.Adp.Backend.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The nesting decision as a pure function (adp-file-nesting Requirement 3): placement is
/// decided from names, kinds and a resolved body, never from the filesystem.
/// </summary>
public class HierarchyNestingTests
{
    private static string? NoBody(string _) => null;

    [Fact]
    public void ARegistration_NestsUnderItsSubjectFile()
    {
        // Arrange.
        var siblings = new[] { ("test.dsl", false), ("test.adp", false) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, _ => "test.dsl");

        // Assert.
        var entry = Assert.Single(placed);
        Assert.Equal("test.adp", entry.Name);
        Assert.Equal("test.dsl", entry.SubjectName);
        Assert.False(entry.IsOrphan);
    }

    /// <summary>
    /// The trap this type exists to avoid: a subject is a FILE, never a folder. A name-based
    /// implementation nests `templates.adp` under the `templates/` directory whose name
    /// matches just as well.
    /// </summary>
    [Fact]
    public void ARegistrationBesideAMatchingDirectory_NestsUnderTheFileNotTheDirectory()
    {
        // Arrange: the shipped azure-pipeline example's exact shape.
        var siblings = new[] { ("templates.adp", false), ("templates.yml", false), ("templates", true) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, _ => "templates.yml");

        // Assert.
        var entry = Assert.Single(placed);
        Assert.Equal("templates.yml", entry.SubjectName);
    }

    [Fact]
    public void AFolderScopedRegistration_StaysInsideItsFolder()
    {
        // Arrange: the bare .adp already sits inside its subject (Requirement 4.2).
        var siblings = new[] { (".adp", false), ("playbooks", true) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, NoBody);

        // Assert.
        var entry = Assert.Single(placed);
        Assert.Null(entry.SubjectName);
        Assert.False(entry.IsOrphan);
    }

    [Fact]
    public void ARegistrationWhoseSubjectIsMissing_StaysVisibleAndMarked()
    {
        // Arrange: a resolved subject that is not among the siblings (Requirements 8.1, 8.2).
        var siblings = new[] { ("test.first.adp", false) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, _ => "test.dsl");

        // Assert.
        var entry = Assert.Single(placed);
        Assert.Null(entry.SubjectName);
        Assert.True(entry.IsOrphan);
    }

    [Fact]
    public void ARegistrationResolvingNoBody_StaysPutWithoutBeingAnOrphan()
    {
        // Arrange: an unknown type resolves nothing, and that is not an error (Requirement 8.4).
        var siblings = new[] { ("mystery.adp", false), ("mystery.dsl", false) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, NoBody);

        // Assert.
        var entry = Assert.Single(placed);
        Assert.Null(entry.SubjectName);
        Assert.False(entry.IsOrphan);
    }

    [Fact]
    public void SeveralRegistrations_AllNestUnderTheOneSubject()
    {
        // Arrange.
        var siblings = new[]
        {
            ("courier.dsl", false),
            ("courier.adp", false),
            ("courier.containers.adp", false),
            ("courier.landscape.adp", false),
        };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, _ => "courier.dsl");

        // Assert.
        Assert.Equal(3, placed.Count);
        Assert.All(placed, entry => Assert.Equal("courier.dsl", entry.SubjectName));
    }

    [Fact]
    public void TheOrder_PutsTheUnqualifiedFormFirst_ThenQualifiersOrdinal()
    {
        // Arrange: the unqualified form is the default Requirement 7.2 activates.
        var names = new[] { "courier.landscape.adp", "courier.adp", "courier.containers.adp" };

        // Act.
        var ordered = names.OrderBy(name => name, Comparer<string>.Create(HierarchyNesting.CompareRegistrations)).ToArray();

        // Assert.
        Assert.Equal(new[] { "courier.adp", "courier.containers.adp", "courier.landscape.adp" }, ordered);
    }

    [Fact]
    public void NonRegistrations_AreNotPlacedAtAll()
    {
        // Arrange: nesting relocates registrations; it never touches ordinary entries.
        var siblings = new[] { ("readme.md", false), ("src", true), ("test.dsl", false) };

        // Act.
        var placed = HierarchyNesting.Assign(siblings, NoBody);

        // Assert.
        Assert.Empty(placed);
    }
}
