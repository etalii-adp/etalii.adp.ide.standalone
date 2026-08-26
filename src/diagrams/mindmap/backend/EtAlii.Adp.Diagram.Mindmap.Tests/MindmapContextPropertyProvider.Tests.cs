using EtAlii.Adp.Backend.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// What the property grid shows for a mindmap node, and what changing one of those values does.
/// </summary>
public class MindmapContextPropertyProviderTests
{
    private const string RootNodeId = "ID_1730044821";
    private const string BackendNodeId = "ID_411002937";

    private static ContextPropertyDefinition Property(IReadOnlyList<ContextPropertyDefinition> properties, string id) =>
        Assert.Single(properties, property => property.Id == id);

    [Fact]
    public async Task ANode_OffersItsTextNotesAndLink()
    {
        // Arrange.
        using var project = new MindmapTestProject();

        // Act.
        var properties = await project.Properties.DescribeAsync(
            project.NodeTarget(BackendNodeId), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Backend", Property(properties, MindmapContextPropertyProvider.TextPropertyId).Value);
        Assert.Equal(ContextPropertyEditor.Text, Property(properties, MindmapContextPropertyProvider.NotesPropertyId).Editor);
        Assert.Contains(properties, property => property.Id == MindmapContextPropertyProvider.LinkPropertyId);
    }

    [Fact]
    public async Task Collapsing_IsShownButNotEditable_BecauseItIsViewStateAndNotUndoable()
    {
        // Arrange.
        // The grid promises every edit is one undo away. Folding writes nothing and lands on no
        // history (Requirements 9.4, 9.6), so offering it here would break that promise - but
        // hiding it would leave a reader wondering where a node's children went.
        using var project = new MindmapTestProject();

        // Act.
        var properties = await project.Properties.DescribeAsync(
            project.NodeTarget(RootNodeId), TestContext.Current.CancellationToken);

        // Assert.
        var folded = Property(properties, MindmapContextPropertyProvider.FoldedPropertyId);
        Assert.False(folded.IsEditable);
        Assert.Contains("view setting", folded.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Equal("View", folded.Group);
    }

    [Fact]
    public async Task TheIdentifier_IsShownButNotEditable_BecauseFreeplaneOwnsIt()
    {
        // Arrange.
        using var project = new MindmapTestProject();

        // Act.
        var properties = await project.Properties.DescribeAsync(
            project.NodeTarget(BackendNodeId), TestContext.Current.CancellationToken);

        // Assert.
        var identifier = Property(properties, MindmapContextPropertyProvider.IdentifierPropertyId);
        Assert.Equal(BackendNodeId, identifier.Value);
        Assert.False(identifier.IsEditable);
    }

    [Fact]
    public async Task SettingTheText_ChangesTheDocument_AndIsOneUndoAway()
    {
        // Arrange.
        using var project = new MindmapTestProject();
        var before = await File.ReadAllTextAsync(project.BodyPath, TestContext.Current.CancellationToken);

        // Act.
        var result = await project.Properties.SetAsync(
            project.NodeTarget(BackendNodeId),
            MindmapContextPropertyProvider.TextPropertyId,
            "Backend services",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Backend services", project.Document.Find(BackendNodeId)!.Text);

        await project.History.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllTextAsync(project.BodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyingTheLink_UnlinksTheNode()
    {
        // Arrange.
        // An emptied link is an unlink, through the same command with nothing in it - so undo
        // puts the old link back whichever way it was removed.
        using var project = new MindmapTestProject();
        await project.Properties.SetAsync(
            project.NodeTarget(BackendNodeId), MindmapContextPropertyProvider.LinkPropertyId, "docs/other.md", TestContext.Current.CancellationToken);

        // Act.
        var result = await project.Properties.SetAsync(
            project.NodeTarget(BackendNodeId), MindmapContextPropertyProvider.LinkPropertyId, "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(project.Document.Find(BackendNodeId)!.Link);
    }

    [Fact]
    public async Task SettingAPropertyOfANodeThatIsGone_IsRefusedRatherThanThrown()
    {
        // Arrange.
        using var project = new MindmapTestProject();

        // Act.
        var result = await project.Properties.SetAsync(
            project.NodeTarget("ID_nonexistent"),
            MindmapContextPropertyProvider.TextPropertyId,
            "Anything",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Error);
    }
}
