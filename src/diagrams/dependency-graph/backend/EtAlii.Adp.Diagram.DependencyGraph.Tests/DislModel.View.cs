using EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity.Oracle;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// A dependency graph's DISL model seen as the tests have always asked about it: its nodes and its dependencies,
/// each by what is written - the id, the label, the place, and a dependency's ends - in the oracle's records, with
/// no line ranges, since nothing in the module splices at one any more.
/// </summary>
internal static class DislModelView
{
    extension(DislModel model)
    {
        /// <summary>The nodes, in the model's order.</summary>
        public IReadOnlyList<DependencyGraphElement> Elements =>
        [
            .. model.Diagram.NodesOfType("Node").Select(node => new DependencyGraphElement(
                DependencyGraphDefinition.WrittenId(node),
                node.ValueOf("label") as string ?? "",
                node.ValueOf("x") is double x ? x : 0d,
                node.ValueOf("row") is long row ? checked((int)row) : 0,
                default)),
        ];

        /// <summary>The dependencies, in the model's order, with their ends as written.</summary>
        public IReadOnlyList<DependencyGraphRelation> Relations =>
        [
            .. model.Diagram.RelationsOfType("DependsOn").Select(relation => new DependencyGraphRelation(
                DependencyGraphDefinition.WrittenId(relation),
                DependencyGraphDefinition.HostText(relation, "writtenFrom"),
                DependencyGraphDefinition.HostText(relation, "writtenTo"),
                relation.ValueOf("label") as string ?? "",
                default)),
        ];
    }
}
