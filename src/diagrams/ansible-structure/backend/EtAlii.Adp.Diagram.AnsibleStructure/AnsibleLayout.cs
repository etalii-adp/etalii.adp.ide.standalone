using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Where every node goes: ranked left to right so the execution story reads in one direction,
/// with inventories and variable folders banded beneath it rather than interleaved
/// (Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// Pure - the definition's model in, boxes out. It reads each node's type and <c>name</c> and the
/// <c>ImportsPlaybook</c> relations that reach a playbook, in the model's order, which is the plugin's
/// (finding B6); the definition names this layout as the plugin's (gap 10), so the ranks are code.
/// </para>
/// <para>
/// No file access, no clock, no randomness, and nothing that
/// depends on which connection is asking. The same tree yields the same picture for everybody,
/// which is what makes two people able to talk about it.
/// </para>
/// <para>
/// <b>The files carry no positions and none are ever written back.</b> This type's subject is an
/// Ansible project, and an Ansible project has nowhere to put a diagram coordinate that would
/// not be ADP graffiti in a file another tool owns. Layout is derived, every time.
/// </para>
/// </remarks>
public static class AnsibleLayout
{
    /// <summary>
    /// The box for every node in <paramref name="diagram"/>, keyed by node id.
    /// </summary>
    public static IReadOnlyDictionary<string, Rect> Compute(DislDiagram diagram, AnsibleMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var scale = metrics ?? AnsibleMetrics.Default;

        var ranks = Ranks(diagram);
        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);

        // The flow: everything that has a rank, laid out in columns.
        var flow = diagram.Nodes
            .Select((node, index) => (Node: node, Index: index))
            .Where(entry => ranks.ContainsKey(entry.Node.Id))
            .GroupBy(entry => ranks[entry.Node.Id])
            .OrderBy(group => group.Key)
            .ToArray();

        var x = 0.0;
        var flowBottom = 0.0;
        foreach (var column in flow)
        {
            // Declaration order within a rank, which the graph already fixed ordinally. Its
            // index is unique, so it needs no tiebreak - and using it keeps a role beside the
            // play that first reached for it rather than sorting the story into alphabetical
            // order.
            var members = column.OrderBy(entry => entry.Index).ToArray();
            var width = members.Max(entry => scale.Measure(NameOf(entry.Node)));

            var y = 0.0;
            foreach ((DislElement node, var _) in members)
            {
                boxes[node.Id] = new Rect(x, y, width, scale.NodeHeight);
                y += scale.NodeHeight + scale.RowGap;
            }

            flowBottom = Math.Max(flowBottom, y - scale.RowGap);
            x += width + scale.RankGap;
        }

        // The band: inventories and variable folders, beneath the flow rather than in it. A
        // play's edge into an inventory drops out of the execution story instead of lengthening
        // it, which is the whole reason these are not ranked (Requirement 6.2).
        var bandY = flowBottom + scale.BandGap;
        var bandX = 0.0;
        foreach (var node in diagram.Nodes.Where(node => !ranks.ContainsKey(node.Id)))
        {
            var width = scale.Measure(NameOf(node));
            boxes[node.Id] = new Rect(bandX, bandY, width, scale.NodeHeight);
            bandX += width + scale.RankGap;
        }

        return boxes;
    }

    /// <summary>
    /// Which column each node belongs in. Entry playbooks first - the ones nothing imports -
    /// then what they import, then plays, then roles, then task files. Inventories and variable
    /// folders get no rank at all, which is how the caller knows they are band material.
    /// </summary>
    private static Dictionary<string, int> Ranks(DislDiagram diagram)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        // Playbook depth by longest path over imports, so a playbook always sits to the right
        // of everything that imports it however many hops away that is.
        // Each playbook is measured by a walk of its own (see BackEdgeIsZeroPerNode): Ansible
        // refuses an import cycle, but ADP reads a folder somebody is still editing, and a
        // half-written cycle must neither hang the diagram nor depend on which playbook came first.
        var imports = diagram.Relations
            .Where(relation => relation is { Type.Name: "ImportsPlaybook", Source: not null, Target: not null })
            .Select(relation => (SourceId: relation.Source!.Id, TargetId: relation.Target!.Id))
            .ToArray();
        var playbooks = diagram.Nodes.Where(node => node.Type.Name == "Playbook").Select(node => node.Id).ToArray();
        var depths = Layering.LongestPath(
            playbooks.Concat(imports.Select(edge => edge.SourceId)),
            imports,
            CyclePolicy.BackEdgeIsZeroPerNode);
        foreach (var playbook in playbooks)
        {
            ranks[playbook] = depths[playbook];
        }

        var deepestPlaybook = ranks.Count == 0 ? 0 : ranks.Values.Max();

        // A play sits one column right of the playbook that declares it, so the file and what
        // it runs read left to right rather than stacked in one column.
        foreach (var play in diagram.Nodes.Where(node => node.Type.Name == "Play"))
        {
            var owner = PlaybookIdOf(play.Id);
            ranks[play.Id] = (ranks.TryGetValue(owner, out var depth) ? depth : deepestPlaybook) + 1;
        }

        var deepest = ranks.Count == 0 ? 0 : ranks.Values.Max();

        foreach (var role in diagram.Nodes.Where(node => node.Type.Name == "Role"))
        {
            ranks[role.Id] = deepest + 1;
        }

        foreach (var file in diagram.Nodes.Where(node => node.Type.Name == "TaskFile"))
        {
            ranks[file.Id] = deepest + 2;
        }

        return ranks;
    }

    /// <summary>What a node is called, which is what its box is measured by.</summary>
    private static string NameOf(DislElement node) => node.ValueOf("name") as string ?? "";

    /// <summary>The playbook a play's id belongs to: <c>play:file#2</c> names <c>playbook:file</c>.</summary>
    private static string PlaybookIdOf(string playId)
    {
        var hash = playId.LastIndexOf('#');
        var path = hash < 0 ? playId["play:".Length..] : playId["play:".Length..hash];
        return $"playbook:{path}";
    }
}
