using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The numbers behind a position, readable and correctable without a text editor
/// (Requirement 15).
/// </summary>
public sealed class WardleyContextPropertyProviderTests : IDisposable
{
    private const string Map = """
        title Tea Shop
        anchor Business [0.95, 0.63]
        component Cup of Tea [0.79, 0.61] label [-30, 12]
        component Kettle [0.43, 0.35] (buy) inertia
        component Power [0.1, 0.7]
        evolve Kettle->Electric Kettle 0.62
        Cup of Tea->Kettle
        Kettle+>Power; cash
        pipeline Kettle
        {
          component Electric Kettle [0.63]
        }

        """;

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), $"wardley-properties-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyContextPropertyProvider _provider;
    private readonly string _path;

    public WardleyContextPropertyProviderTests()
    {
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        _documents = _services.GetRequiredService<IWardleyDocumentStore>();
        _provider = _services.GetServices<IContextPropertyProvider>().OfType<WardleyContextPropertyProvider>().Single();
        _path = IoPath.Combine(_root, "tea.owm");
        File.WriteAllText(_path, Map);
    }

    public void Dispose()
    {
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    // ---- what is shown ------------------------------------------------------------------------

    [Fact]
    public void ItContributesToTheDiagramElementScope_AndRegistersInOneLine()
    {
        // Arrange, act and assert. Requirement 15.1.
        Assert.Equal(ContextScope.DiagramElement, _provider.Scope);
        Assert.Single(_services.GetServices<IContextPropertyProvider>().OfType<WardleyContextPropertyProvider>());
    }

    [Fact]
    public async Task AComponentShows_WhatRequirement152Asks()
    {
        // Act.
        var rows = await Describe(IdOf("Kettle"));

        // Assert.
        Assert.Equal("Kettle", Value(rows, WardleyContextPropertyProvider.NamePropertyId));
        Assert.Equal("component", Value(rows, WardleyContextPropertyProvider.KindPropertyId));
        Assert.Equal("0.43", Value(rows, WardleyContextPropertyProvider.VisibilityPropertyId));
        Assert.Equal("0.35", Value(rows, WardleyContextPropertyProvider.MaturityPropertyId));
        Assert.Equal("Custom Built", Value(rows, WardleyContextPropertyProvider.StagePropertyId));
        Assert.Equal("0.62", Value(rows, WardleyContextPropertyProvider.EvolvePropertyId));
        Assert.Equal("Electric Kettle", Value(rows, WardleyContextPropertyProvider.EvolveNamePropertyId));
        Assert.Equal("true", Value(rows, WardleyContextPropertyProvider.InertiaPropertyId));
        Assert.Equal("buy", Value(rows, WardleyContextPropertyProvider.DecoratorsPropertyId));
    }

    [Fact]
    public async Task KindAndDecoratorsAreSeparateRows()
    {
        // Act. Requirement 15.2 - a market is a component that carries `(market)`, not a fourth
        // kind, and showing them as one row would be showing the file wrongly.
        var rows = await Describe(IdOf("Kettle"));

        // Assert.
        Assert.Equal("component", Value(rows, WardleyContextPropertyProvider.KindPropertyId));
        Assert.Equal("buy", Value(rows, WardleyContextPropertyProvider.DecoratorsPropertyId));
    }

    [Fact]
    public async Task AnAbsentPropertyIsNotContributedAtAll()
    {
        // Act. Requirement 15.6 - the distinction between "has no evolve" and "has an empty
        // one" is one the contract keeps deliberately.
        var kettle = await Describe(IdOf("Kettle"));
        var power = await Describe(IdOf("Power"));

        // Assert.
        Assert.Contains(kettle, row => row.Id == WardleyContextPropertyProvider.EvolvePropertyId);
        Assert.DoesNotContain(power, row => row.Id == WardleyContextPropertyProvider.EvolvePropertyId);
        Assert.DoesNotContain(power, row => row.Id == WardleyContextPropertyProvider.LabelOffsetPropertyId);
        Assert.Contains(
            await Describe(IdOf("Cup of Tea")),
            row => row.Id == WardleyContextPropertyProvider.LabelOffsetPropertyId);
    }

    [Fact]
    public async Task InertiaIsAlwaysThere_BecauseAbsentIsFalse()
    {
        // Act. The one row that is contributed even when the document says nothing: a toggle
        // with no off state could never be turned off.
        var power = await Describe(IdOf("Power"));

        // Assert.
        var inertia = power.Single(row => row.Id == WardleyContextPropertyProvider.InertiaPropertyId);
        Assert.Equal("false", inertia.Value);
        Assert.Equal(ContextPropertyEditor.Toggle, inertia.Editor);
    }

    [Fact]
    public async Task TheRowsAreGroupedTheWayRequirement159Asks()
    {
        // Act.
        var rows = await Describe(IdOf("Kettle"));

        // Assert.
        Assert.Equal("Identity", Group(rows, WardleyContextPropertyProvider.NamePropertyId));
        Assert.Equal("Position", Group(rows, WardleyContextPropertyProvider.MaturityPropertyId));
        Assert.Equal("Position", Group(rows, WardleyContextPropertyProvider.StagePropertyId));
        Assert.Equal("Strategy", Group(rows, WardleyContextPropertyProvider.EvolvePropertyId));
        Assert.Equal("Strategy", Group(rows, WardleyContextPropertyProvider.DecoratorsPropertyId));
    }

    [Fact]
    public async Task ThisTypeNeedsNoNewEditor()
    {
        // Act. Requirement 15.10 - Line, Text and Toggle serve everything here, and this module
        // is not the reason a Choice editor lands early.
        var rows = new List<ContextPropertyDefinition>();
        foreach (var id in new[] { IdOf("Kettle"), IdOf("Cup of Tea"), LinkId(), ChildId() })
        {
            rows.AddRange(await Describe(id));
        }

        // Assert.
        Assert.All(rows, row => Assert.Contains(
            row.Editor,
            new[] { ContextPropertyEditor.Line, ContextPropertyEditor.Text, ContextPropertyEditor.Toggle }));
    }

    [Fact]
    public async Task NoContributedReasonIsBlank()
    {
        // Act. Both IsEditable and the client's PropertyRow test the reason's LENGTH, so a
        // reason of " " would render the row editable AND let the write through.
        var rows = new List<ContextPropertyDefinition>();
        foreach (var id in new[] { IdOf("Kettle"), IdOf("Cup of Tea"), IdOf("Power"), LinkId(), ChildId(), PipelineId() })
        {
            rows.AddRange(await Describe(id));
        }

        // Assert.
        Assert.All(rows, row => Assert.False(
            row.ReadOnlyReason.Length > 0 && string.IsNullOrWhiteSpace(row.ReadOnlyReason),
            $"'{row.Id}' carries a whitespace-only reason, which reads as editable."));
    }

    [Fact]
    public async Task TheEditableRowsAreExactlyTheAuthorsOwnClaims()
    {
        // Act. Requirement 15.5.
        var rows = await Describe(IdOf("Kettle"));

        // Assert.
        Assert.True(Row(rows, WardleyContextPropertyProvider.NamePropertyId).IsEditable);
        Assert.True(Row(rows, WardleyContextPropertyProvider.VisibilityPropertyId).IsEditable);
        Assert.True(Row(rows, WardleyContextPropertyProvider.MaturityPropertyId).IsEditable);
        Assert.True(Row(rows, WardleyContextPropertyProvider.EvolvePropertyId).IsEditable);
        Assert.True(Row(rows, WardleyContextPropertyProvider.InertiaPropertyId).IsEditable);
        Assert.True(Row(rows, WardleyContextPropertyProvider.DecoratorsPropertyId).IsEditable);
        Assert.False(Row(rows, WardleyContextPropertyProvider.StagePropertyId).IsEditable);
    }

    [Fact]
    public async Task TheDerivedStageRefusesAWrite_WithAReasonNamingMaturity()
    {
        // Act.
        var rows = await Describe(IdOf("Kettle"));
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.StagePropertyId, "Commodity");

        // Assert. The reason is shown AND thrown: the panel puts it beside the value, and the
        // resolver returns it verbatim when it refuses the write.
        Assert.Contains("Change Maturity instead", Row(rows, WardleyContextPropertyProvider.StagePropertyId).ReadOnlyReason, StringComparison.Ordinal);
        Assert.False(result.IsSuccess);
        Assert.Contains("Maturity", result.Error, StringComparison.Ordinal);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALinkShowsItsEndsItsKindAndItsContext()
    {
        // Act. Requirement 15.3.
        var rows = await Describe(LinkId());

        // Assert.
        Assert.Equal("Kettle", Value(rows, WardleyContextPropertyProvider.LinkSourcePropertyId));
        Assert.Equal("Power", Value(rows, WardleyContextPropertyProvider.LinkTargetPropertyId));
        Assert.Equal("flow", Value(rows, WardleyContextPropertyProvider.LinkKindPropertyId));
        Assert.Equal("cash", Value(rows, WardleyContextPropertyProvider.LinkContextPropertyId));
        Assert.True(Row(rows, WardleyContextPropertyProvider.LinkContextPropertyId).IsEditable);
        Assert.False(Row(rows, WardleyContextPropertyProvider.LinkSourcePropertyId).IsEditable);
    }

    [Fact]
    public async Task APipelineChildsVisibilityIsShownAsItsParents_WithAReasonSayingSo()
    {
        // Act. Requirement 15.4.
        var rows = await Describe(ChildId());

        // Assert.
        var visibility = Row(rows, WardleyContextPropertyProvider.VisibilityPropertyId);
        Assert.Equal("0.43", visibility.Value);
        Assert.False(visibility.IsEditable);
        Assert.Contains("takes its visibility from its parent", visibility.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Contains("Kettle", visibility.ReadOnlyReason, StringComparison.Ordinal);
        Assert.True(Row(rows, WardleyContextPropertyProvider.MaturityPropertyId).IsEditable);
    }

    [Fact]
    public async Task APipelineShowsItsChildrenAndTheirPositions()
    {
        // Act. Requirement 15.4.
        var rows = await Describe(PipelineId());

        // Assert.
        Assert.Equal("Kettle", Value(rows, WardleyContextPropertyProvider.NamePropertyId));
        Assert.Equal("0.43", Value(rows, WardleyContextPropertyProvider.VisibilityPropertyId));
        Assert.Equal("Electric Kettle (0.63)", Value(rows, WardleyContextPropertyProvider.PipelineChildrenPropertyId));
    }

    [Fact]
    public async Task InReadOnlyMode_EveryRowIsStillContributed_AndNoneIsEditable()
    {
        // Arrange. Requirement 15.11 - the panel must still answer what the element is.
        var editable = await Describe(IdOf("Kettle"));
        File.SetAttributes(_path, FileAttributes.ReadOnly);

        try
        {
            // Act.
            var rows = await Describe(IdOf("Kettle"));
            var refused = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.MaturityPropertyId, "0.9");

            // Assert.
            Assert.Equal(editable.Select(row => row.Id), rows.Select(row => row.Id));
            Assert.All(rows, row => Assert.False(row.IsEditable, row.Id));
            Assert.False(refused.IsSuccess);
        }
        finally
        {
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }

    // ---- what editing one does ------------------------------------------------------------------

    [Fact]
    public async Task EditingMaturity_AndDraggingToTheSamePlace_AreOneCommandAndOneUndo()
    {
        // Arrange. Requirement 15.7's own claim, tested by doing both and comparing the bytes.
        var history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);

        // Act.
        var typed = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.MaturityPropertyId, "0.42");
        var afterTyping = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);
        var afterUndo = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);

        await history.ExecuteAsync(
            new MoveWardleyElementCommand(_path, IdOf("Kettle"), 0.43d, 0.42d), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(typed.IsSuccess, typed.Error);
        Assert.Equal(Map, afterUndo);
        Assert.Equal(afterTyping, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditingVisibility_LeavesMaturityWhereItWas()
    {
        // Act. Both coordinates travel in one command, so the row that was not touched has to
        // carry the value it already had.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.VisibilityPropertyId, "0.5");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("component Kettle [0.5, 0.35] (buy) inertia", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedCoordinateLeavesTheDocumentUnchanged()
    {
        // Act. Requirement 15.7.
        var offScale = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.MaturityPropertyId, "1.4");
        var notANumber = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.MaturityPropertyId, "quite evolved");

        // Assert.
        Assert.False(offScale.IsSuccess);
        Assert.Contains("off the map", offScale.Error, StringComparison.Ordinal);
        Assert.False(notANumber.IsSuccess);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARenameThroughTheGrid_RewritesEveryReference()
    {
        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.NamePropertyId, "Boiler");

        // Assert. The same command the menu's Rename uses, so the references travel with it.
        Assert.True(result.IsSuccess, result.Error);
        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.Contains("component Boiler [0.43, 0.35]", text, StringComparison.Ordinal);
        Assert.Contains("Cup of Tea->Boiler", text, StringComparison.Ordinal);
        Assert.Contains("evolve Boiler->Electric Kettle", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARenameToATakenName_IsRefusedWithTheReason()
    {
        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.NamePropertyId, "Power");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already has an element", result.Error, StringComparison.Ordinal);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditingTheDecoratorsRow_IsOneUndo()
    {
        // Arrange. The row shows "what is set" (Requirement 15.2), so replacing two words with
        // two others is one edit and must be one press of Ctrl+Z.
        var history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);

        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.DecoratorsPropertyId, "market, outsource");
        var after = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("(market)", after, StringComparison.Ordinal);
        Assert.Contains("(outsource)", after, StringComparison.Ordinal);
        Assert.DoesNotContain("(buy)", after, StringComparison.Ordinal);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMisspelledDecorator_IsRefusedRatherThanQuietlyDropped()
    {
        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.DecoratorsPropertyId, "buy, outsorce");

        // Assert. Keeping the ones it recognised would leave the user looking at a row that did
        // not do what they typed and did not say so.
        Assert.False(result.IsSuccess);
        Assert.Contains("outsorce", result.Error, StringComparison.Ordinal);
        Assert.Contains("market", result.Error, StringComparison.Ordinal);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearingTheDecoratorsRow_TakesThemAllOff()
    {
        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.DecoratorsPropertyId, "");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("(buy)", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TogglingInertia_WritesAndRemovesTheWord()
    {
        // Act.
        await Set(IdOf("Kettle"), WardleyContextPropertyProvider.InertiaPropertyId, "false");
        var off = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        await Set(IdOf("Kettle"), WardleyContextPropertyProvider.InertiaPropertyId, "true");

        // Assert.
        Assert.DoesNotContain("inertia", off, StringComparison.Ordinal);
        Assert.Contains("inertia", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyingTheEvolveRow_ClearsTheStatement()
    {
        // Act. The row's own way of saying "this is no longer heading anywhere".
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.EvolvePropertyId, "");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("evolve Kettle", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingTheEvolveTarget_KeepsTheNameItArrivesUnder()
    {
        // Act.
        var result = await Set(IdOf("Kettle"), WardleyContextPropertyProvider.EvolvePropertyId, "0.8");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("evolve Kettle->Electric Kettle 0.8", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingALinksContext_RewritesTheOneStatement()
    {
        // Act.
        var result = await Set(LinkId(), WardleyContextPropertyProvider.LinkContextPropertyId, "money");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("Kettle+>Power; money", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingAPipelineChildsMaturity_MovesOnlyItsOwnNumber()
    {
        // Act. Requirement 5.4 - a child has no visibility of its own to move.
        var result = await Set(ChildId(), WardleyContextPropertyProvider.MaturityPropertyId, "0.71");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("  component Electric Kettle [0.71]", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnElementThatIsGone_IsRefusedWithAMessage()
    {
        // Act.
        var result = await Set("no-such-id", WardleyContextPropertyProvider.NamePropertyId, "Anything");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("That element is no longer on this map.", result.Error);
    }

    [Fact]
    public async Task APropertyThisElementDoesNotHave_IsRefusedRatherThanIgnored()
    {
        // Act.
        var result = await Set(LinkId(), WardleyContextPropertyProvider.InertiaPropertyId, "true");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(Map, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private string IdOf(string name) => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name)
        .Id;

    private string LinkId() => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.Link && entry.Key.Contains("Power", StringComparison.Ordinal))
        .Id;

    private string ChildId() => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.PipelineChild)
        .Id;

    private string PipelineId() => _documents
        .Identities(_path)
        .Single(entry => entry.Kind == WardleyIdentityKind.Pipeline)
        .Id;

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement, _path, IsContainer: false, SourceId: default, _root, default, elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> Describe(string elementId) =>
        await _provider.DescribeAsync(Target(elementId), TestContext.Current.CancellationToken);

    private Task<ContextPropertyResult> Set(string elementId, string propertyId, string value) =>
        _provider.SetAsync(Target(elementId), propertyId, value, TestContext.Current.CancellationToken).AsTask();

    private static ContextPropertyDefinition Row(IReadOnlyList<ContextPropertyDefinition> rows, string id) =>
        rows.Single(row => row.Id == id);

    private static string Value(IReadOnlyList<ContextPropertyDefinition> rows, string id) => Row(rows, id).Value;

    private static string Group(IReadOnlyList<ContextPropertyDefinition> rows, string id) => Row(rows, id).Group;
}
