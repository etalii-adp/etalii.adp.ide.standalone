using System.Text;
using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The widths of the columns of items (DESL 0.2 section 7.7.1): <c>addColumnWidth</c>, <c>setColumnWidth</c> and
/// <c>resetColumnWidth</c> write the registration's <c>widths</c> block alone, each a step of the history, and the page
/// is sent the widths the registration holds.
/// </summary>
public sealed class ColumnOperationsTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task SettingChangingAndResetting_AreStepsOfTheHistory_InTheRegistrationAlone_AndThePageIsSentTheWidths()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, ColumnOperations.AddColumnWidth, "document", ("level", "1"), ("width", "360"));
        await RunAsync(page, ColumnOperations.SetColumnWidth, "column/1", ("width", "420"));
        await RunAsync(page, ColumnOperations.AddColumnWidth, "document", ("level", "2"), ("width", "300"));
        var set = await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken);
        var sent = page.Models.Count;
        await RunAsync(page, ColumnOperations.ResetColumnWidth, "column/1");
        var shown = await page.ModelAsync(sent, model => model.Widths.Count == 1);

        // Assert.
        Assert.EndsWith("filters:\r\n  Open security: #security @proposed sort:state\r\n  Backend: item:back* sort:number\r\nwidths:\r\n  1: 420\r\n  2: 300\r\n", set, StringComparison.Ordinal);
        Assert.EndsWith("widths:\r\n  2: 300\r\n", await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal([new HierarchySurfaceColumnWidth("column/2", 2, 300)], shown?.Widths);
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        for (var step = 0; step < 4; step++)
        {
            Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ColumnOperations.AddColumnWidth, "document", "0", "300", "A column is named by its depth, a whole number from 1.")]
    [InlineData(ColumnOperations.AddColumnWidth, "document", "1", "119", "A column is 120 to 2000 pixels wide.")]
    [InlineData(ColumnOperations.SetColumnWidth, "column/1", "", "2001", "A column is 120 to 2000 pixels wide.")]
    [InlineData(ColumnOperations.SetColumnWidth, "column/1", "", "wide", "A column is 120 to 2000 pixels wide.")]
    [InlineData(ColumnOperations.SetColumnWidth, "column/3", "", "300", HierarchicalRequirementGestures.Drifted)]
    [InlineData(ColumnOperations.ResetColumnWidth, "column/3", "", "", HierarchicalRequirementGestures.Drifted)]
    public async Task WhatCannotBeWritten_IsRefusedAtOnce(string operation, string targetId, string level, string width, string expected)
    {
        // Arrange: the first column has a width; the third has none.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        await RunAsync(page, ColumnOperations.AddColumnWidth, "document", ("level", "1"), ("width", "360"));
        await page.ModelAsync(0, model => model.Widths.Count == 1);
        (string Name, string Value)[] parameters = [.. new[] { ("level", level), ("width", width) }.Where(parameter => parameter.Item2.Length > 0)];

        // Act.
        (_, string refusal) = page.Gesture(operation, targetId, parameters);

        // Assert.
        Assert.Equal(expected, refusal);
    }

    [Fact]
    public async Task AFirstWidthForAColumnThatHasOne_IsAChangeMadeAgainstAnOlderFile()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = Encoding.UTF8.GetBytes(await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken) + "widths:\r\n  1: 360\r\n");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["level"] = "1", ["width"] = "400" };

        // Act.
        var edit = ColumnOperations.Apply(body, "web-shop.md", registration, "web-shop.adp", ColumnOperations.AddColumnWidth, "document", parameters);

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, edit.Refusal);
        Assert.Null(edit.RegistrationAfter);
    }

    [Fact]
    public async Task AWidthThatIsNotANumberInRange_IsRead_AsAColumnWithTheHostsWidth()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = Encoding.UTF8.GetBytes(await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken) + "widths:\r\n  1: 400\r\n  2: wide\r\n  3: 50\r\n  0: 300\r\n  4: 2000\r\n");

        // Act.
        var document = HierarchicalRequirementBody.Read(body, "web-shop.md", registration, "web-shop.adp").Document;

        // Assert.
        Assert.Equal([new Column(1, 400), new Column(4, 2000)], document.Columns);
    }

    private static async Task RunAsync(RequirementPage page, string operation, string targetId, params (string Name, string Value)[] parameters)
    {
        (ShortGuid editId, string refusal) = page.Gesture(operation, targetId, parameters);
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.True(outcome?.Written, outcome?.Error);
    }
}
