using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// <c>storeFilter</c>, <c>renameFilter</c> and <c>removeFilter</c> make the registration's bytes the conformance fixture
/// <c>hierarchical-requirement-filters</c> states and write nothing to the Markdown file (spec 029 T103, FR-026, FR-048);
/// what FBL refuses is refused, and nothing is written.
/// </summary>
public sealed class FilterOperationsTests
{
    private const string Filters = "hierarchical-requirement-filters";

    /// <summary>
    /// Every step of the fixture that writes: the step whose registration it starts from (-1 for the input), the gesture,
    /// its target and params, and the step whose registration it writes, -1 for the input.
    /// </summary>
    public static TheoryData<int, string, string, string, int> Steps() => new()
    {
        { -1, FilterOperations.StoreFilter, "document", "name= Open security |query= #security state:proposed sort:state ", 0 },
        { 0, FilterOperations.StoreFilter, "document", "name=Keyboard and forms|query=#ux item:inter* sort:title", 1 },
        { 1, FilterOperations.RenameFilter, "filter/Keyboard and forms", "name=Interface", 2 },
        { 1, FilterOperations.StoreFilter, "document", "name=Security: open|query=#security state:proposed", 6 },
        { 1, FilterOperations.RemoveFilter, "filter/Open security", "", 8 },
        { 0, FilterOperations.RemoveFilter, "filter/Open security", "", -1 },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(int from, string operation, string targetId, string parameters, int step)
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Filters, "requirements.md"), TestContext.Current.CancellationToken);
        var registration = await RegistrationAsync(from);

        // Act.
        var edit = FilterOperations.Apply(body, "requirements.md", registration, "requirements.adp", operation, targetId, Params(parameters));

        // Assert.
        Assert.Equal("", edit.Refusal);
        Assert.Null(edit.Body);
        Assert.Equal(registration, edit.RegistrationBefore);
        Assert.Equal(Encoding.UTF8.GetString(await RegistrationAsync(step)), Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.RegistrationAfter)));
    }

    [Theory]
    [InlineData(FilterOperations.RenameFilter, "filter/Keyboard and forms", "name=Open security")]
    [InlineData(FilterOperations.StoreFilter, "document", "name=Accepted|query=state: accepted")]
    public async Task WhatFblRefuses_WritesNothing(string operation, string targetId, string parameters)
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Filters, "requirements.md"), TestContext.Current.CancellationToken);

        // Act.
        var edit = FilterOperations.Apply(body, "requirements.md", await RegistrationAsync(1), "requirements.adp", operation, targetId, Params(parameters));

        // Assert.
        Assert.NotEqual("", edit.Refusal);
        Assert.Null(edit.RegistrationAfter);
    }

    [Fact]
    public async Task WhatCannotBeWritten_IsRefused_AndAGoneFilter_IsAChangeMadeAgainstAnOlderFile()
    {
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Filters, "requirements.md"), TestContext.Current.CancellationToken);
        var registration = await RegistrationAsync(0);

        Assert.Equal("A filter needs a name.", Refusal(FilterOperations.StoreFilter, "document", "name= |query=#ux"));
        Assert.Equal("There is no filter to store.", Refusal(FilterOperations.StoreFilter, "document", "name=Empty|query= "));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal(FilterOperations.RemoveFilter, "filter/Gone", ""));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal(FilterOperations.RenameFilter, "filter/open security", "name=Open"));

        string Refusal(string operation, string targetId, string parameters) =>
            FilterOperations.Apply(body, "requirements.md", registration, "requirements.adp", operation, targetId, Params(parameters)).Refusal;
    }

    /// <summary>The registration as the fixture's step leaves it, or its input for -1.</summary>
    private static async Task<byte[]> RegistrationAsync(int step)
    {
        if (step < 0) return await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Filters, "requirements.adp"), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Filters, "fixture.json"), TestContext.Current.CancellationToken));
        return Encoding.UTF8.GetBytes(json.RootElement.GetProperty("steps")[step].GetProperty("expect").GetString()!);
    }

    private static Dictionary<string, string> Params(string parameters) =>
        parameters.Length == 0
            ? []
            : parameters.Split('|').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}

/// <summary>
/// Storing a filter on the page is a step of the history that writes the registration alone (spec 029 T103, FR-048):
/// one undo takes it back, and the Markdown file is never written.
/// </summary>
public sealed class FilterGesturesTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task StoringRenamingAndRemoving_AreStepsOfTheHistory_InTheRegistrationAlone()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, FilterOperations.StoreFilter, "document", "name=Done|query=state:done sort:title");
        var stored = await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken);
        await RunAsync(page, FilterOperations.RenameFilter, "filter/Backend", "name=Server");
        await RunAsync(page, FilterOperations.RemoveFilter, "filter/Open security", "");
        var changed = await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Assert.
        Assert.EndsWith("  Backend: item:back* sort:number\r\n  Done: state:done sort:title\r\n", stored, StringComparison.Ordinal);
        Assert.EndsWith("filters:\r\n  Server: item:back* sort:number\r\n  Done: state:done sort:title\r\n", changed, StringComparison.Ordinal);
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        for (var step = 0; step < 3; step++)
        {
            Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFilterThePageDoesNotShow_IsRefusedAtOnce()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Act.
        (_, string refusal) = page.Gesture(FilterOperations.RemoveFilter, "filter/Gone");

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, refusal);
    }

    private static async Task RunAsync(RequirementPage page, string operation, string targetId, string parameters)
    {
        var pairs = parameters.Length == 0 ? [] : parameters.Split('|').Select(pair => pair.Split('=', 2)).Select(pair => (pair[0], pair[1])).ToArray();
        (ShortGuid editId, string refusal) = page.Gesture(operation, targetId, pairs);
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.True(outcome?.Written, outcome?.Error);
    }
}
