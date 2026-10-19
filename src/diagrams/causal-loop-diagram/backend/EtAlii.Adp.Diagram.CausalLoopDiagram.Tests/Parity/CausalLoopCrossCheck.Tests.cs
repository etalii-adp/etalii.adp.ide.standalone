using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for the causal loop diagram: the ids per type the
/// vendored example binding reads from every <c>.cld</c> file of the repository are the ones the module reads. It moved
/// here from <c>EtAlii.Adp.Specification.Fbl.Tests</c> so that no core project references a tool module (code-structure
/// T287); its divergences are still listed in that project's <c>RealFiles/divergences.json</c>.
/// </summary>
public class CausalLoopCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("causal-loop");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("causal-loop", file, element => element.Id, Module);

    /// <summary>
    /// The module has no parser of its own since causal-loop-disl-fbl task 7: its side is what its body reads, the
    /// module's own options over the module's embedded binding. The binding's ids are DISL's (causal-loop-diagram.dis:
    /// natural, with its prefixes), which the module's model gives its links and loops but not its variables.
    /// </summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var loop = CausalLoopBody.Parse(text).Read();
        foreach (var variable in loop.Variables) ModuleCrossCheck.Add(ids, "Variable", $"variable:{variable.Id}");
        foreach (var link in loop.Links) ModuleCrossCheck.Add(ids, "Link", link.Id);
        foreach (var cycle in loop.Loops) ModuleCrossCheck.Add(ids, "Loop", cycle.Id);
        return ids;
    }
}
