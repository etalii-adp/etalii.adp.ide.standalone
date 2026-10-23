using EtAlii.Adp.Files;
using EtAlii.Adp.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// A change another program makes to an open document is shown without reopening it (spec 029 FR-022, US2 scenario 2,
/// R13): what is collapsed is kept by number, so an item an agent moved is still collapsed; the page's own write and a
/// write of identical bytes send nothing; and a change that makes the file unreadable keeps the last page, read-only.
/// </summary>
public sealed class OutsideChangeTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AnOutsideChange_IsShownWithoutReopening_AndAMovedItemKeepsItsFold()
    {
        // Arrange: the web shop open, with ITEM-0003 collapsed by its registration, nested in ITEM-0002.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var opened = page.Session.Model();
        Assert.Equal("ITEM-0002", opened.Nodes.Single(node => node.Id == "ITEM-0003").ParentId);
        Assert.True(opened.Nodes.Single(node => node.Id == "ITEM-0003").Collapsed);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act: an agent moves ITEM-0003 with its requirements to the top level, at the end, and renames REQ-0001.
        _folder.WriteAsAnAgent(text =>
        {
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var guest = $"    - ITEM-0003 Guest checkout{newline}      + REQ-0005 A customer can order without an account. state:rejected{newline}      + REQ-NEW A guest is offered an account after the order. #customer category:functional{newline}";
            var last = $"  - REQ-0010 Personal data is removed on request within 30 days. #legal #privacy category:non-functional{newline}";
            Assert.Contains(guest, text, StringComparison.Ordinal);
            return text
                .Replace(guest, "", StringComparison.Ordinal)
                .Replace(last, last + $"- ITEM-0003 Guest checkout{newline}  - REQ-0005 A customer can order without an account. state:rejected{newline}  - REQ-NEW A guest is offered an account after the order. #customer category:functional{newline}", StringComparison.Ordinal)
                .Replace("REQ-0001 A customer finds a product by its name or its description.", "REQ-0001 A customer finds a product by its name.", StringComparison.Ordinal);
        });

        // Assert: the page shows it, as a change from outside, the moved item still collapsed and at the top level.
        var shown = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Title == "A customer finds a product by its name."));
        Assert.NotNull(shown);
        Assert.Equal(HierarchySurfaceCauses.Outside, shown.Cause);
        var moved = shown.Nodes.Single(node => node.Id == "ITEM-0003");
        Assert.Equal("", moved.ParentId);
        Assert.True(moved.Collapsed);
        Assert.True(shown.Nodes.Single(node => node.Id == "REQ-0002").Collapsed);
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThePagesOwnWrite_AndAWriteOfTheSameBytes_SendNothingFromOutside()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Act: a title changed on the page, then both files saved again by another program with the bytes they hold.
        (ShortGuid editId, string refusal) = page.Gesture(TextOperations.SetTitle, "REQ-0004", ("title", "An order is confirmed by e-mail."));
        Assert.Equal("", refusal);
        Assert.True((await page.OutcomeAsync(editId))?.Written);
        var edited = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Title == "An order is confirmed by e-mail."));
        Assert.Equal(HierarchySurfaceCauses.Edit, edited?.Cause);
        var sent = page.Models.Count;
        await WebShopFolder.ReplaceAsync(_folder.Body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        await WebShopFolder.ReplaceAsync(_folder.Registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));

        // Assert: the edit was sent once, as an edit; neither the watcher hearing that write nor the identical bytes sent another.
        Assert.Null(await page.ModelAsync(sent, _ => true, TimeSpan.FromMilliseconds(750)));
        Assert.DoesNotContain(page.Models, model => model.Cause == HierarchySurfaceCauses.Outside);
    }

    [Fact]
    public async Task AFileReadHalfWritten_IsReadAgain_BeforeThePageIsMadeReadOnly()
    {
        // Arrange: the page's retries wait on a clock that stands still until this test moves it.
        var clock = new FakeTimeProvider();
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration, time: clock);
        var whole = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act: another program has emptied the file and not yet written it again, as a save that truncates first does.
        await WebShopFolder.ReplaceAsync(_folder.Body, []);

        // Assert: nothing is sent while the read waits to be tried again.
        Assert.Null(await page.ModelAsync(0, _ => true, TimeSpan.FromMilliseconds(500)));

        // Act: the save finishes, and the retry comes round.
        await WebShopFolder.ReplaceAsync(_folder.Body, [.. whole[..^1], (byte)' ', whole[^1]]);
        var shown = await Eventually.ValueAsync(
            () =>
            {
                clock.Advance(WatchedFile.BetweenReadAttempts);
                return page.Models.FirstOrDefault();
            },
            TestContext.Current.CancellationToken);

        // Assert: the page as the file now is, never read-only on the way.
        Assert.NotNull(shown);
        Assert.Equal("", shown.ReadOnlyReason);
        Assert.DoesNotContain(page.Models, model => model.ReadOnlyReason.Length > 0);
    }

    [Fact]
    public async Task AChangeThatMakesTheFileUnreadable_KeepsTheLastPage_ReadOnly_UntilItReadsAgain()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var opened = page.Session.Model();
        var readable = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act: another program leaves bytes that are not UTF-8 at the end of the file.
        await File.WriteAllBytesAsync(_folder.Body, [.. readable, 0xFF, 0xFE], TestContext.Current.CancellationToken);

        // Assert: the same boxes, read-only, saying why; and no change is taken while it cannot be read.
        var kept = await page.ModelAsync(0, model => model.ReadOnlyReason.Length > 0);
        Assert.NotNull(kept);
        Assert.Equal(opened.Nodes, kept.Nodes);
        Assert.Contains("not valid UTF-8", kept.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Equal(kept.ReadOnlyReason, page.Gesture(TextOperations.SetTitle, "REQ-0001", ("title", "Lost.")).Refusal);

        // Act and assert: readable again, it is the page again.
        await File.WriteAllBytesAsync(_folder.Body, readable, TestContext.Current.CancellationToken);
        var again = await page.ModelAsync(page.Models.Count, model => model.ReadOnlyReason.Length == 0);
        Assert.NotNull(again);
        Assert.Equal(opened.Nodes.Select(node => (node.Id, node.Title)), again.Nodes.Select(node => (node.Id, node.Title)));
    }
}
