using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The refusals of drawing a flow derived from the definition's <c>connect</c> constraints equal what the hand-written
/// handler refused (supply-chain-disl-fbl, task 7): for every document of the parity corpus and every pair of ids it
/// writes, with the empty id and one nothing holds, the definition refuses exactly when
/// <see cref="HandWrittenSupplyChainEdits.ConnectSupplyChainNodes"/> did, and with its sentence.
/// </summary>
/// <remarks>
/// The id of the new flow is one the document does not hold, so the host's own refusal of a taken id is not compared:
/// it is no constraint's. Nor is the refusal of a document that cannot be read, which the edit handler makes before any
/// constraint is asked: there the handler of today is compared with the hand-written one. An end the tool finds no entry for refuses before any constraint is asked, in the sentence of
/// <c>connectBetweenNodes</c>.
/// </remarks>
public class SupplyChainDerivedGesturesTests
{
    private const string Body = "parity.supply";
    private const string NewId = "parity-new";
    private const string Unknown = "parity-unknown";

    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryConnectRefusal_IsTheHandWrittenOne(string name)
    {
        // Arrange.
        var text = await SupplyChainCorpus.TextAsync(name, TestContext.Current.CancellationToken);
        var outcome = SupplyChainParser.Outcome(LineDocument.Parse(text));
        var model = outcome.Model;
        var diagram = SupplyChainDefinition.Diagram(model);
        var ids = Ids(model);
        List<string> mismatches = [];

        // Act.
        foreach (var from in ids)
        {
            foreach (var to in ids)
            {
                var handled = await new HandWrittenSupplyChainEdits.ConnectSupplyChainNodes(new SupplyChainParityStore(text))
                    .ExecuteAsync(new ConnectSupplyChainNodesCommand(Body, from, to, NewId), TestContext.Current.CancellationToken);
                var expected = handled.IsSuccess ? "allowed" : $"refused: {handled.Error}";
                var actual = outcome.IsUsable ? Derived(diagram, from, to) : await HandledAsync(text, from, to);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    mismatches.Add($"connect {from} to {to}\n  hand-written {expected}\n  definition   {actual}");
                }
            }
        }

        // Assert.
        Assert.True(mismatches.Count == 0, $"{name}: {mismatches.Count} of {ids.Count * ids.Count} differ\n{string.Join("\n", mismatches)}");
    }

    /// <summary>The corpus reaches every refusal of drawing a flow, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheCorpus_ReachesEveryConnectRefusal()
    {
        // Arrange.
        HashSet<string> answers = new(StringComparer.Ordinal);

        // Act.
        foreach (var row in SupplyChainCorpus.Names())
        {
            var model = SupplyChainParser.Parse(LineDocument.Parse(await SupplyChainCorpus.TextAsync(row.Data, TestContext.Current.CancellationToken)));
            var diagram = SupplyChainDefinition.Diagram(model);
            var ids = Ids(model);
            answers.UnionWith(ids.SelectMany(from => ids.Select(to => Derived(diagram, from, to))));
        }

        // Assert.
        Assert.Contains("allowed", answers);
        Assert.Contains("refused: A flow runs from one node to another.", answers);
        Assert.Contains("refused: A node does not supply itself.", answers);
        Assert.Contains("refused: These two are already connected by a flow in this direction.", answers);
    }

    /// <summary>What today's handler answers to drawing a flow from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static async Task<string> HandledAsync(string text, string from, string to)
    {
        var handled = await new ConnectSupplyChainNodesCommandHandler(new SupplyChainParityStore(text))
            .ExecuteAsync(new ConnectSupplyChainNodesCommand(Body, from, to, NewId), TestContext.Current.CancellationToken);
        return handled.IsSuccess ? "allowed" : $"refused: {handled.Error}";
    }

    /// <summary>What the definition says of drawing a flow from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static string Derived(DislDiagram diagram, string from, string to)
    {
        if (SupplyChainDefinition.ElementOf(diagram, from) is not { } source || SupplyChainDefinition.ElementOf(diagram, to) is not { } target)
        {
            return "refused: A flow runs from one node to another.";
        }

        return GestureConstraintEvaluator.Connect(SupplyChainDefinition.Specification, diagram, "Flow", source, target) is { } refusal
            ? $"refused: {refusal.Message}"
            : "allowed";
    }

    /// <summary>Every id the document writes, once, with the empty id and one nothing holds.</summary>
    private static List<string> Ids(SupplyChainModel model) =>
    [
        .. model.Groups.Select(group => group.Id)
            .Concat(model.Nodes.Select(node => node.Id))
            .Concat(model.Flows.Select(flow => flow.Id))
            .Append("")
            .Append(Unknown)
            .Distinct(StringComparer.Ordinal),
    ];
}
