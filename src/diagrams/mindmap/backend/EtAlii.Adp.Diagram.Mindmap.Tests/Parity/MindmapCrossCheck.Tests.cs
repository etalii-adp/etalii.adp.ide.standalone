using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for the mind map: the ids per type the vendored
/// Freeplane binding reads from every <c>.mm</c> file of the repository are the ones <see cref="MindmapDocument"/>
/// reads, a branch by its ends. It moved here from <c>EtAlii.Adp.Specification.Fbl.Tests</c> so that no core project
/// references a tool module (code-structure T287); its divergences are still listed in that project's
/// <c>RealFiles/divergences.json</c>.
/// </summary>
public class MindmapCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("mindmap");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("mindmap", file, element => element.IsRelation ? $"branch:{element.Source}->{element.Target}" : element.IdIsStored ? element.Id : null, Module);

    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var map = MindmapDocument.Parse(text, assignMissingIds: false);
        foreach (var node in map.Nodes.Where(n => n.Id.Length > 0))
        {
            ModuleCrossCheck.Add(ids, "Node", node.Id);
            if (node.Parent is { Id.Length: > 0 } parent) ModuleCrossCheck.Add(ids, "Branch", $"branch:{parent.Id}->{node.Id}");
        }
        return ids;
    }
}
