using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The three writer defects of finding B13 (supply-chain-disl-fbl Requirement 5.9, Q6), each through the module's
/// commands on a real file: the writer found keys and sections by the start of a line rather than by the parsed tree, so
/// a value that spans lines was replaced on its first line only, a line inside one that begins with a key's name was
/// taken for the key, and an entry written as a flow mapping was given a block key under it. Through the binding an
/// entry written as a flow mapping is refused with a sentence instead, which Requirement 5.9 allows: the binding reads
/// such an entry as one value, so it has no key of it to splice; what a `set` does to one is a question for FBL.
/// </summary>
public sealed class SupplyChainWriterDefectsTests : IDisposable
{
    private readonly SupplyChainDocumentStore _store = new(new FileWriter());
    private SupplyChainTestFolder? _folder;

    public void Dispose() => _folder?.Dispose();

    [Fact]
    public async Task AValueThatSpansLines_IsReplacedWhole()
    {
        // Arrange.
        var before = Open("multi-line-values.supply");

        // Act.
        await Dispatch(new SetSupplyChainPropertyCommand(_folder!.Body, "mine", SupplyChainKeys.Description, "An open pit."));

        // Assert: the block's lines went with it, and nothing beside it changed.
        var after = Read();
        Assert.Empty(after.Problems);
        var mine = after.NodeOf("mine")!;
        Assert.Equal("An open pit.", mine.Description);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal(5, mine.Quantity);
        Assert.Equal(before.NodeOf("plant")!.Description, after.NodeOf("plant")!.Description);
    }

    [Fact]
    public async Task ALineInsideAValue_IsNotTakenForAKey()
    {
        // Arrange: the description's block holds a line `name: a line of the description, not a key`.
        var before = Open("multi-line-values.supply");

        // Act.
        await Dispatch(new SetSupplyChainPropertyCommand(_folder!.Body, "mine", SupplyChainKeys.Name, "Open-pit mine"));

        // Assert.
        var after = Read();
        Assert.Empty(after.Problems);
        Assert.Equal("Open-pit mine", after.NodeOf("mine")!.Name);
        Assert.Equal(before.NodeOf("mine")!.Description, after.NodeOf("mine")!.Description);
    }

    [Theory]
    [InlineData(SupplyChainKeys.Name, "Open-pit mine")]
    [InlineData(SupplyChainKeys.Quantity, "7")]
    public async Task AnEntryWrittenAsAFlowMapping_IsRefused_AndNothingIsWritten(string key, string value)
    {
        // Arrange: `- { id: mine, type: source, name: Mine, group: north }`, a value it has and a key it lacks.
        Open("flow-mapping-entry.supply");
        var before = await File.ReadAllBytesAsync(_folder!.Body, TestContext.Current.CancellationToken);
        var dispatcher = TestHistory.Dispatcher(s => { s.AddSupplyChain(); s.AddSingleton<ISupplyChainDocumentStore>(_store); });

        // Act.
        var result = await dispatcher.DispatchAsync(new SetSupplyChainPropertyCommand(_folder.Body, "mine", key, value), TestContext.Current.CancellationToken);

        // Assert: refused with a sentence, rather than a block key written under the flow mapping.
        Assert.False(result.IsSuccess);
        Assert.Contains($"'{key}'", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    private SupplyChainModel Open(string fixture)
    {
        _folder = new SupplyChainTestFolder(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), fixture);
        return Read();
    }

    private SupplyChainModel Read() => SupplyChainReading.Parse(File.ReadAllText(_folder!.Body));

    private async Task Dispatch(ICommand command)
    {
        var dispatcher = TestHistory.Dispatcher(s => { s.AddSupplyChain(); s.AddSingleton<ISupplyChainDocumentStore>(_store); });
        var result = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }
}
