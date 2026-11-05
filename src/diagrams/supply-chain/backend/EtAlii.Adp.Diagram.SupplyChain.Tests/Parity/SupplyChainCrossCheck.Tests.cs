using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The cross-check of supply-chain-disl-fbl task 10: for every <c>.supply</c> file of the repository, the groups, nodes
/// and flows the module's binding (<c>supply-chain.fbl</c>, embedded) reads, each by the id it is written with, are the
/// ones the module (<see cref="SupplyChainReading"/>) reads from that binding's reading. Every difference is a divergence
/// listed in <c>RealFiles/divergences.json</c> of <c>EtAlii.Adp.Specification.Fbl.Tests</c>.
/// </summary>
public class SupplyChainCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("supply-chain");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("supply-chain", file, IdOf, Module);

    /// <summary>A group, a node or a flow by the id it is written with; the document, an entry without an id and one that is not a mapping have none the module knows.</summary>
    private static string? IdOf(FblElement element) =>
        element.Type is "Group" or "Node" or "Flow" && element.Attributes.GetValueOrDefault(SupplyChainWritten.StoredId) is string { Length: > 0 } stored ? stored : null;

    /// <summary>The module's side: every group, node and flow with an id, by the id it is written with.</summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var model = SupplyChainReading.Parse(text);
        foreach (var group in model.Groups.Where(group => group.Id.Length > 0)) ModuleCrossCheck.Add(ids, "Group", group.Id);
        foreach (var node in model.Nodes.Where(node => node.Id.Length > 0)) ModuleCrossCheck.Add(ids, "Node", node.Id);
        foreach (var flow in model.Flows.Where(flow => flow.Id.Length > 0)) ModuleCrossCheck.Add(ids, "Flow", flow.Id);
        return ids;
    }
}
