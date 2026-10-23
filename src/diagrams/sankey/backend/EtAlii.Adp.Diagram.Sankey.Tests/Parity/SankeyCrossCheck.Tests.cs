using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.TestSupport;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The cross-check of sankey-disl-fbl Requirement 1.5: for every <c>.skv</c> file of the repository, the nodes and flows
/// the module's binding (<c>sankey.fbl</c>, embedded and read by nothing but tests until task 10) reads, each by the id
/// it is written with, are the ones <see cref="SankeyParser"/> reads. Every difference is a divergence listed in
/// <c>RealFiles/divergences.json</c> of <c>EtAlii.Adp.Specification.Fbl.Tests</c>.
/// </summary>
public class SankeyCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("sankey");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("sankey", file, IdOf, Module);

    /// <summary>
    /// A node by the id it is written with; a flow by the id it states, else its two ends as written, as the module and
    /// the definition's identity expression name it. The document and an entry that is not a mapping have no id the
    /// module knows.
    /// </summary>
    private static string? IdOf(FblElement element) => element.Type switch
    {
        "Node" => Text(element, "storedId") is { Length: > 0 } stored ? stored : null,
        "Flow" => Text(element, "statedId") is { Length: > 0 } stated ? stated : SankeyFlow.IdOf(Text(element, "from"), Text(element, "to")),
        _ => null,
    };

    /// <summary>The module's side: every node with an id and every flow, by the ids <see cref="SankeyParser"/> gives them.</summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var model = SankeyParser.Parse(LineDocument.Parse(text));
        foreach (var node in model.Nodes.Where(node => node.Id.Length > 0)) ModuleCrossCheck.Add(ids, "Node", node.Id);
        foreach (var flow in model.Flows) ModuleCrossCheck.Add(ids, "Flow", flow.Id);
        return ids;
    }

    /// <summary>An attribute as text: a string as it is, any other value as the invariant text of what FBL read.</summary>
    private static string Text(FblElement element, string attribute) => element.Attributes.GetValueOrDefault(attribute) switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable value => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        var other => other.ToString() ?? "",
    };
}
