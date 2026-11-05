using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// Undo of an edit made on the page (spec 029 FR-025, US2 scenario 3, R10, R12): it gives the file's bytes back exactly
/// and redo makes the edit again; once another program has written the file, it gives the history's own Outdated message
/// once, writes nothing, and the edits before that are gone from the history.
/// </summary>
public sealed class UndoTests : IDisposable
{
    private const string Outdated = "web-shop.md was changed by another program, so the edits made to it before that can no longer be undone.";

    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Undo_GivesTheBytesBack_AndRedo_MakesTheEditAgain_EachShownAsAnEdit()
    {
        // Arrange: a description changed on the page.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var before = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        (ShortGuid editId, _) = page.Gesture(TextOperations.SetDescription, "REQ-0007", ("description", "A forgotten password is reset by e-mail."));
        Assert.True((await page.OutcomeAsync(editId))?.Written);
        var after = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        Assert.NotEqual(before, after);

        // Act and assert: undone, the bytes are those from before, and the page shows it as an edit.
        var sent = page.Models.Count;
        Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        var undone = await page.ModelAsync(sent, model => model.Nodes.Single(node => node.Id == "REQ-0007").Description == "");
        Assert.Equal(HierarchySurfaceCauses.Edit, undone?.Cause);

        // Act and assert: redone, the edit's bytes again.
        Assert.True((await page.History.RedoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(after, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWriteACommandAnnounced_IsShownAsAnEdit_WhenTheWatcherHearsItBeforeTheStoreSaysSo()
    {
        // Arrange: the bytes an undo is about to write, announced as a command of the module announces them.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var restored = System.Text.Encoding.UTF8.GetBytes(_folder.Text.Replace("Order data is kept for seven years", "Order data is kept for ten years", StringComparison.Ordinal));
        page.Documents.WillWrite(_folder.Body, restored);

        // Act: written, and heard by the watcher alone; the store's word after the write never comes.
        await WebShopFolder.ReplaceAsync(_folder.Body, restored);

        // Assert: shown as the edit it is, so a field open on the page is not offered the choice a change from outside gets.
        var shown = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Title.StartsWith("Order data is kept for ten years", StringComparison.Ordinal)));
        Assert.Equal(HierarchySurfaceCauses.Edit, shown?.Cause);
    }

    [Fact]
    public async Task UndoPastAnOutsideChange_GivesTheOutdatedMessageOnce_AndWritesNothing()
    {
        // Arrange: two titles changed on the page, then the file written by an agent.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        (ShortGuid first, _) = page.Gesture(TextOperations.SetTitle, "REQ-0001", ("title", "A customer finds a product by its name."));
        Assert.True((await page.OutcomeAsync(first))?.Written);
        (ShortGuid second, _) = page.Gesture(TextOperations.SetTitle, "REQ-0004", ("title", "An order is confirmed by e-mail."));
        Assert.True((await page.OutcomeAsync(second))?.Written);
        _folder.WriteAsAnAgent(text => text.Replace("Order data is kept for seven years", "Order data is kept for ten years", StringComparison.Ordinal));
        var agents = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act.
        var undo = await page.History.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: the history's own message, nothing written, and nothing left to undo, so it is said once.
        Assert.False(undo.IsSuccess);
        Assert.Equal(Outdated, undo.Error);
        Assert.Equal(agents, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        Assert.False(page.History.CanUndo);
        Assert.Equal("There is nothing to undo.", (await page.History.UndoAsync(TestContext.Current.CancellationToken)).Error);
        Assert.Equal(agents, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }
}
