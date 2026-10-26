using EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// The bytes of a <c>.supply</c> survive being read and edited through the binding (supply-chain-disl-fbl Requirement
/// 5.8): every document of the corpus, the pair of line-ending fixtures and the one without a final newline among them,
/// saved without a change is written back as it was read, and every edit made to it and undone through the host's
/// restore command gives back the bytes it had.
/// </summary>
public sealed class SupplyChainRoundTripTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.SupplyChainRoundTrip");

    public static TheoryData<string> Corpus() => SupplyChainCorpus.Names();

    private string Body => Path.Combine(_folder.Path, "body.supply");

    public void Dispose() => _folder.Dispose();

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task ADocumentSavedUnchanged_IsWrittenAsItWasRead(string name)
    {
        // Arrange.
        var original = await OriginalAsync(name);
        var store = new SupplyChainDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(Body, TestContext.Current.CancellationToken);
        var saved = await store.SaveAsync(Body, entry.Document, TestContext.Current.CancellationToken);

        // Assert: the body holds the text as it was read, and a body that cannot be read is not written at all.
        Assert.Equal(TranscriptText.Decode(original), entry.Document.Text);
        Assert.True(saved.Error.Length == 0 || !entry.IsUsable, saved.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EveryEditUndone_GivesBackTheBytes(string name)
    {
        // Arrange.
        var original = await OriginalAsync(name);
        var model = SupplyChainReading.Parse(TranscriptText.Decode(original));
        List<string> wrong = [];
        var edited = 0;

        // Act.
        foreach ((string edit, ICommand command) in EditsOf(model))
        {
            var store = new SupplyChainDocumentStore(new FileWriter());
            var dispatcher = TestHistory.Dispatcher(s => { s.AddSupplyChain(); s.AddSingleton<ISupplyChainDocumentStore>(store); });
            var result = await dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
            if (!result.IsSuccess || result.Inverse is not ICommand inverse)
            {
                continue;
            }

            edited++;
            var undone = await dispatcher.DispatchAsync(inverse, TestContext.Current.CancellationToken);
            var restored = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
            if (!undone.IsSuccess || !original.AsSpan().SequenceEqual(restored))
            {
                wrong.Add($"{edit}: {(undone.IsSuccess ? "the bytes differ" : undone.Error)}");
                await File.WriteAllBytesAsync(Body, original, TestContext.Current.CancellationToken);
            }
        }

        // Assert: a document that reads was edited, otherwise the undo proves nothing.
        Assert.True(edited > 0 || model.Nodes.Count == 0, $"{name}: no edit applied");
        Assert.True(wrong.Count == 0, $"{name}: {wrong.Count} of {edited} undone edits differ\n{string.Join("\n", wrong)}");
    }

    private async Task<byte[]> OriginalAsync(string name)
    {
        var source = new SupplyChainTranscript().Corpus().Single(candidate => candidate.Name == name);
        var original = await source.Read(TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Body, original, TestContext.Current.CancellationToken);
        return original;
    }

    /// <summary>The edits of the transcript's script, once each, at the document's first entries.</summary>
    private IEnumerable<(string Edit, ICommand Command)> EditsOf(SupplyChainModel model)
    {
        yield return ("add a node", new AddSupplyChainNodeCommand(Body, SupplyChainNodeTypes.Hub, 500.5, 300.5, "round-trip-node"));
        yield return ("add a group", new AddSupplyChainGroupCommand(Body, 40.5, 40.5, "round-trip-group"));
        yield return ("arrange", new ArrangeSupplyChainCommand(Body));
        foreach (var node in model.Nodes.Where(node => node.Id.Length > 0).Take(2))
        {
            yield return ($"rename {node.Id}", new SetSupplyChainPropertyCommand(Body, node.Id, SupplyChainKeys.Name, "Round trip: #1"));
            yield return ($"describe {node.Id}", new SetSupplyChainPropertyCommand(Body, node.Id, SupplyChainKeys.Description, ""));
            yield return ($"step {node.Id}", new StepSupplyChainValueCommand(Body, node.Id, 3));
            yield return ($"move {node.Id}", new MoveSupplyChainEntryCommand(Body, node.Id, 120.5, 80.5));
            yield return ($"group {node.Id}", new GroupSupplyChainNodeCommand(Body, node.Id, "Round trip", "round-trip-new-group"));
            yield return ($"remove {node.Id}", new RemoveSupplyChainEntryCommand(Body, node.Id));
        }

        if (model.Nodes.FirstOrDefault(node => node.Id.Length > 0) is { } first && model.Nodes.LastOrDefault(node => node.Id.Length > 0) is { } last)
        {
            yield return ($"connect {last.Id} to {first.Id}", new ConnectSupplyChainNodesCommand(Body, last.Id, first.Id, "round-trip-flow"));
        }

        foreach (var flow in model.Flows.Where(flow => flow.Id.Length > 0).Take(1))
        {
            yield return ($"set {flow.Id} product", new SetSupplyChainPropertyCommand(Body, flow.Id, SupplyChainKeys.Product, "Round trip"));
            yield return ($"step {flow.Id}", new StepSupplyChainValueCommand(Body, flow.Id, -1));
            yield return ($"remove {flow.Id}", new RemoveSupplyChainEntryCommand(Body, flow.Id));
        }

        foreach (var group in model.Groups.Where(group => group.Id.Length > 0).Take(1))
        {
            yield return ($"move {group.Id}", new MoveSupplyChainEntryCommand(Body, group.Id, 300.5, 200.5));
            yield return ($"remove {group.Id}", new RemoveSupplyChainEntryCommand(Body, group.Id));
        }
    }
}
