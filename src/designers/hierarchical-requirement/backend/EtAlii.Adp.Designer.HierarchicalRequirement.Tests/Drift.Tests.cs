using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// An edit made on the page against a file another program has written since is refused (spec 029 FR-024, R10, R12):
/// with R10's sentence, nothing written, and the page read again so it shows the file as it is. An edit of an entry the
/// file no longer has is refused the same way, at once.
/// </summary>
public sealed class DriftTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AnEditMadeAgainstAFileAnAgentHasSinceWritten_IsRefused_NothingIsWritten_AndTheNewPageIsSent()
    {
        // Arrange: the agent's write lands after the page took the edit and before the edit is made, which is the race
        // the check exists for.
        byte[]? agents = null;
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration, beforeEachCommand: async () =>
        {
            if (agents is null)
            {
                _folder.WriteAsAnAgent(text => text.Replace("REQ-0009 Order data is kept for seven years", "REQ-0009 Order data is kept for ten years", StringComparison.Ordinal));
                agents = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
            }
        });

        // Act.
        (ShortGuid editId, string refusal) = page.Gesture(TextOperations.SetTitle, "REQ-0004", ("title", "An order is confirmed by e-mail."));

        // Assert: taken, then refused with R10's sentence; the file is the agent's; the page shows the agent's change.
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.NotNull(outcome);
        Assert.False(outcome.Written);
        Assert.Equal("The file changed while you were editing, so this change was not written. The page now shows the file as it is.", outcome.Error);
        Assert.Equal(agents, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        var shown = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Title.StartsWith("Order data is kept for ten years", StringComparison.Ordinal)));
        Assert.NotNull(shown);
        Assert.DoesNotContain(shown.Nodes, node => node.Title == "An order is confirmed by e-mail.");
        Assert.False(page.History.CanUndo);
    }

    [Fact]
    public async Task AnEditOfAnEntryTheFileNoLongerHas_IsRefusedAtOnce()
    {
        // Arrange: an agent removed REQ-0007, and the page has shown it.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        _folder.WriteAsAnAgent(text => string.Join('\n', text.Split('\n').Where(line => !line.Contains("REQ-0007", StringComparison.Ordinal))));
        Assert.NotNull(await page.ModelAsync(0, model => model.Nodes.All(node => node.Id != "REQ-0007")));
        var before = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act: the text a person kept, committed against the number that is gone.
        (_, string refusal) = page.Gesture(TextOperations.SetTitle, "REQ-0007", ("title", "Customers sign in with a passkey."));

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, refusal);
        Assert.Equal(before, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }
}
