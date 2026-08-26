namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Where every node goes: ranked left to right so the execution story reads in one direction,
/// with inventories and variable folders banded beneath it rather than interleaved
/// (Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// Pure - a graph in, boxes out. No file access, no clock, no randomness, and nothing that
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
    /// The box for every node in <paramref name="graph"/>, keyed by node id.
    /// </summary>
    public static IReadOnlyDictionary<string, AnsibleBox> Compute(AnsibleGraph graph, AnsibleMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var scale = metrics ?? AnsibleMetrics.Default;

        var ranks = Ranks(graph);
        var boxes = new Dictionary<string, AnsibleBox>(StringComparer.Ordinal);

        // The flow: everything that has a rank, laid out in columns.
        var flow = graph.Nodes
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
            var width = members.Max(entry => scale.Measure(entry.Node.Name));

            var y = 0.0;
            foreach (var (node, _) in members)
            {
                boxes[node.Id] = new AnsibleBox(x, y, width, scale.NodeHeight);
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
        foreach (var node in graph.Nodes.Where(node => !ranks.ContainsKey(node.Id)))
        {
            var width = scale.Measure(node.Name);
            boxes[node.Id] = new AnsibleBox(bandX, bandY, width, scale.NodeHeight);
            bandX += width + scale.RankGap;
        }

        return boxes;
    }

    /// <summary>
    /// Which column each node belongs in. Entry playbooks first - the ones nothing imports -
    /// then what they import, then plays, then roles, then task files. Inventories and variable
    /// folders get no rank at all, which is how the caller knows they are band material.
    /// </summary>
    private static Dictionary<string, int> Ranks(AnsibleGraph graph)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        // Playbook depth by longest path over imports, so a playbook always sits to the right
        // of everything that imports it however many hops away that is.
        var importers = graph.Edges
            .Where(edge => edge.Kind == AnsibleEdgeKind.ImportsPlaybook && edge.TargetId.Length > 0)
            .ToLookup(edge => edge.TargetId, edge => edge.SourceId, StringComparer.Ordinal);

        var playbooks = graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.Playbook).ToArray();
        foreach (var playbook in playbooks)
        {
            ranks[playbook.Id] = Depth(playbook.Id, importers, []);
        }

        var deepestPlaybook = ranks.Count == 0 ? 0 : ranks.Values.Max();

        // A play sits one column right of the playbook that declares it, so the file and what
        // it runs read left to right rather than stacked in one column.
        foreach (var play in graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.Play))
        {
            var owner = PlaybookIdOf(play.Id);
            ranks[play.Id] = (ranks.TryGetValue(owner, out var depth) ? depth : deepestPlaybook) + 1;
        }

        var deepest = ranks.Count == 0 ? 0 : ranks.Values.Max();

        foreach (var role in graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.Role))
        {
            ranks[role.Id] = deepest + 1;
        }

        foreach (var file in graph.Nodes.Where(node => node.Kind == AnsibleNodeKind.TaskFile))
        {
            ranks[file.Id] = deepest + 2;
        }

        return ranks;
    }

    /// <summary>
    /// How far from an entry point this playbook is, by the longest path of imports.
    /// <paramref name="visiting"/> makes an import cycle terminate rather than recurse for ever -
    /// Ansible would refuse such a project, but ADP is reading a folder somebody is still
    /// editing, and a half-written cycle must not hang the diagram.
    /// </summary>
    private static int Depth(string id, ILookup<string, string> importers, HashSet<string> visiting)
    {
        if (!visiting.Add(id))
        {
            return 0;
        }

        try
        {
            var parents = importers[id].ToArray();
            return parents.Length == 0 ? 0 : parents.Max(parent => Depth(parent, importers, visiting)) + 1;
        }
        finally
        {
            visiting.Remove(id);
        }
    }

    /// <summary>The playbook a play's id belongs to: <c>play:file#2</c> names <c>playbook:file</c>.</summary>
    private static string PlaybookIdOf(string playId)
    {
        var hash = playId.LastIndexOf('#');
        var path = hash < 0 ? playId["play:".Length..] : playId["play:".Length..hash];
        return $"playbook:{path}";
    }
}
