using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The property grid (causal-loop-diagram Requirement 8): a variable's label, a link's weight
/// with its polarity and delay readable, and a loop's identifier, name, computed polarity and the
/// stated one where they differ.
/// </summary>
public class CausalLoopContextPropertiesTests : IDisposable
{
    /// <summary>
    /// R1 claims reinforcing and the arrows agree; B2's two positive links make it arithmetically
    /// reinforcing, so its B claim disagrees. One document carries both cases.
    /// </summary>
    private const string Corpus =
        "causal-loop 1\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "variable crowding \"Crowding\"\r\n"
        + "link population -> births + weight=1.5\r\n"
        + "link births -> population +\r\n"
        + "link population -> crowding +\r\n"
        + "link crowding -> population +\r\n"
        + "loop R1 \"Births beget births\" population births\r\n"
        + "loop B2 \"Crowding checks growth\" population crowding\r\n";

    private readonly string _root;
    private readonly string _path;
    private readonly ServiceProvider _provider;
    private readonly CausalLoopDocumentStore _store = new();
    private readonly CausalLoopContextPropertyProvider _properties;

    public CausalLoopContextPropertiesTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "feedback.cld");
        File.WriteAllText(_path, Corpus);

        _provider = new ServiceCollection().AddCommands().AddCausalLoop().BuildServiceProvider();
        _properties = new CausalLoopContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(), _store);
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private ContextTarget Target(string elementId) =>
        new(ContextScope.DiagramElement, _path, false, ShortGuid.NewShortGuid(), _root, default, elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> Describe(string elementId) =>
        await _properties.DescribeAsync(Target(elementId), TestContext.Current.CancellationToken);

    private static ContextPropertyDefinition Row(IReadOnlyList<ContextPropertyDefinition> rows, string id) =>
        Assert.Single(rows, row => row.Id == id);

    // ---- a variable --------------------------------------------------------------------------

    [Fact]
    public async Task AVariable_ShowsBothItsLabelAndItsIdentifierEditable()
    {
        // Act.
        var rows = await Describe("variable:population");

        // Assert. The visible name is the label, renamed in place; the identifier is the stable
        // key a link refers to, editable here for the author who wants to restate it - which
        // carries every link and loop with it.
        var label = Row(rows, CausalLoopContextPropertyProvider.VariableLabelProperty);
        Assert.Equal("Population", label.Value);
        Assert.True(label.IsEditable);

        var id = Row(rows, CausalLoopContextPropertyProvider.VariableIdProperty);
        Assert.Equal("population", id.Value);
        Assert.True(id.IsEditable);
    }

    /// <summary>
    /// Requirement 8.1 and 8.6. The label is prose nothing refers to, so setting it is a one-line
    /// edit that leaves every reference standing - unlike renaming, which moves them.
    /// </summary>
    [Fact]
    public async Task SettingALabel_LandsAsACommand_AndLeavesTheIdentifierAlone()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("variable:population"),
            CausalLoopContextPropertyProvider.VariableLabelProperty,
            "People, total",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);

        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.Contains("variable population \"People, total\"", text, StringComparison.Ordinal);
        Assert.Contains("link births -> population +", text, StringComparison.Ordinal);
        Assert.Contains("loop R1 \"Births beget births\" population births", text, StringComparison.Ordinal);
    }

    // ---- a link ------------------------------------------------------------------------------

    [Fact]
    public async Task ALink_ShowsItsWeight_WithItsPolarityAndDelayReadableBesideIt()
    {
        // Act.
        var rows = await Describe("link:population|births");

        // Assert.
        // Requirement 8.2: the weight is settable, and the other two are readable there.
        var weight = Row(rows, CausalLoopContextPropertyProvider.LinkWeightProperty);
        Assert.Equal("1.5", weight.Value);
        Assert.True(weight.IsEditable);

        var polarity = Row(rows, CausalLoopContextPropertyProvider.LinkPolarityProperty);
        Assert.Equal("positive", polarity.Value);
        Assert.Equal(ContextPropertyEditor.Choice, polarity.Editor);
        Assert.Contains("negative", polarity.Choices);

        var delayed = Row(rows, CausalLoopContextPropertyProvider.LinkDelayedProperty);
        Assert.Equal("false", delayed.Value);
        Assert.Equal(ContextPropertyEditor.Toggle, delayed.Editor);

        // The ends are the link's identity, and the row says so.
        var from = Row(rows, CausalLoopContextPropertyProvider.LinkFromProperty);
        Assert.False(from.IsEditable);
        Assert.NotEqual("", from.ReadOnlyReason);
    }

    /// <summary>
    /// An unweighted link is not a link of weight zero, so an unwritten weight shows as empty
    /// rather than as a "0" the author never typed.
    /// </summary>
    [Fact]
    public async Task AnUnweightedLink_ShowsAnEmptyWeightRatherThanZero()
    {
        // Act.
        var weight = Row(
            await Describe("link:births|population"),
            CausalLoopContextPropertyProvider.LinkWeightProperty);

        // Assert.
        Assert.Equal("", weight.Value);
    }

    [Fact]
    public async Task SettingAWeight_RecordsItInTheDocument()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("link:births|population"),
            CausalLoopContextPropertyProvider.LinkWeightProperty,
            "0.25",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("weight=0.25", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearingAWeight_RemovesTheAnnotationRatherThanWritingZero()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("link:population|births"),
            CausalLoopContextPropertyProvider.LinkWeightProperty,
            "",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);

        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("weight=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("weight=0", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requirement 8.3. The refusal is worth reading in full: it says the number is an annotation
    /// and that blank removes it, so a user who typed prose learns what the field is for.
    /// </summary>
    [Fact]
    public async Task AWeightThatIsNotANumber_IsRefusedBeforeAnythingIsWritten()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("link:population|births"),
            CausalLoopContextPropertyProvider.LinkWeightProperty,
            "strong",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("never evaluates", result.Error, StringComparison.Ordinal);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettingAPolarityFromTheGrid_ChangesWhatTheLinkAsserts()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("link:population|births"),
            CausalLoopContextPropertyProvider.LinkPolarityProperty,
            "negative",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("link population -> births -", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    // ---- a loop, and the disagreement ---------------------------------------------------------

    /// <summary>
    /// Requirement 8.4 and the reason Requirement 3.3 exists. B2 runs through two positive links,
    /// so the arithmetic makes it reinforcing while its identifier claims balancing. The grid is
    /// where a reader meets that without opening the problems panel — and neither side is
    /// corrected for them.
    /// </summary>
    [Fact]
    public async Task ALoopWhoseLabelDisagreesWithItsArrows_ShowsBothPolarities()
    {
        // Act.
        var rows = await Describe("loop:B2");

        // Assert.
        Assert.Equal("reinforcing", Row(rows, CausalLoopContextPropertyProvider.LoopComputedProperty).Value);

        var stated = Row(rows, CausalLoopContextPropertyProvider.LoopStatedProperty);
        Assert.Equal("balancing", stated.Value);
        Assert.Contains("Neither is corrected for you", stated.ReadOnlyReason, StringComparison.Ordinal);

        // The document still says B2: reporting is not correcting.
        Assert.Contains("loop B2", await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>
    /// The row appears only when there is something to see. A permanent "Stated: reinforcing"
    /// beside "Computed: reinforcing" is noise, and noise is what a reader learns to skip.
    /// </summary>
    [Fact]
    public async Task ALoopWhoseLabelAgrees_ShowsOnePolarityAndNotTwo()
    {
        // Act.
        var rows = await Describe("loop:R1");

        // Assert.
        Assert.Equal("reinforcing", Row(rows, CausalLoopContextPropertyProvider.LoopComputedProperty).Value);
        Assert.DoesNotContain(rows, row => row.Id == CausalLoopContextPropertyProvider.LoopStatedProperty);
    }

    [Fact]
    public async Task TheComputedPolarity_IsNeverEditable_BecauseItIsArithmetic()
    {
        // Act.
        var computed = Row(
            await Describe("loop:R1"), CausalLoopContextPropertyProvider.LoopComputedProperty);

        var result = await _properties.SetAsync(
            Target("loop:R1"),
            CausalLoopContextPropertyProvider.LoopComputedProperty,
            "balancing",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(computed.IsEditable);
        Assert.Contains("Change an arrow", computed.ReadOnlyReason, StringComparison.Ordinal);
        Assert.False(result.IsSuccess);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The identifier is the claim, so restating it is how an author accepts the arithmetic —
    /// which is the one editable half of the disagreement.
    /// </summary>
    [Fact]
    public async Task RestatingALoopsIdentifier_ResolvesTheDisagreementTheAuthorsWay()
    {
        // Act.
        var result = await _properties.SetAsync(
            Target("loop:B2"),
            CausalLoopContextPropertyProvider.LoopIdentifierProperty,
            "R2",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);

        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.Contains("loop R2 \"Crowding checks growth\" population crowding", text, StringComparison.Ordinal);

        // And the disagreement is gone, because the arrows now agree with the claim.
        Assert.DoesNotContain(
            await Describe("loop:R2"),
            row => row.Id == CausalLoopContextPropertyProvider.LoopStatedProperty);
    }

    [Fact]
    public async Task ALoopWhoseCycleHasAnUnstatedLink_ReadsAsUndecidableRatherThanGuessed()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, Corpus.Replace(
            "link births -> population +", "link births -> population", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        _store.Reload(_path);

        // Act.
        var computed = Row(
            await Describe("loop:R1"), CausalLoopContextPropertyProvider.LoopComputedProperty);

        // Assert.
        // "Unknown" is not "none": a parity that cannot be counted is not a count of zero.
        Assert.Equal(CausalLoopContextPropertyProvider.Undecidable, computed.Value);
    }

    // ---- the seam -----------------------------------------------------------------------------

    /// <summary>
    /// The fixture is deliberately a file whose <i>content</i> this module could parse. A foreign
    /// document full of unreadable text would leave this test passing for the wrong reason: the
    /// provider would find no such variable and return nothing whether or not it checked the
    /// extension at all.
    /// </summary>
    [Fact]
    public async Task ADocumentOfAnotherType_IsNotAnswered_EvenWhenItsContentWouldParse()
    {
        // Arrange.
        var other = IoPath.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(other, Corpus, TestContext.Current.CancellationToken);

        // Act.
        var rows = await _properties.DescribeAsync(
            new ContextTarget(
                ContextScope.DiagramElement, other, false, ShortGuid.NewShortGuid(), _root, default, "variable:population"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(rows);
    }

    [Fact]
    public void TheProviderIsRegistered()
    {
        // Act & assert.
        Assert.Contains(
            _provider.GetServices<IContextPropertyProvider>(),
            provider => provider is CausalLoopContextPropertyProvider);
    }
}
