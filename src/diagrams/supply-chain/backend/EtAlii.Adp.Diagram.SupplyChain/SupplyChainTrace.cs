namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The chain through whatever is selected: everything that supplies it, directly or not, and
/// everything it supplies - the highlight a supply chain is read by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Upstream and downstream are walked separately</b>, each along the flows in one direction
/// only. Walking both ways from every node reached would light the whole connected graph, which
/// says nothing; following goods says exactly what depends on what.
/// </para>
/// <para>
/// <b>What a selection traces from.</b> A node traces from itself. A flow traces upstream from its
/// supplier and downstream from its consumer, and is itself selected. A group traces from all of
/// its members at once - the region's whole exposure.
/// </para>
/// </remarks>
public sealed class SupplyChainTrace
{
    /// <summary>The marking of something selected.</summary>
    public const string Selected = "selected";

    /// <summary>The marking of something that supplies the selection.</summary>
    public const string Upstream = "upstream";

    /// <summary>The marking of something the selection supplies.</summary>
    public const string Downstream = "downstream";

    /// <summary>The marking of a group with a member on the traced chain.</summary>
    public const string Related = "related";

    /// <summary>The marking of everything off the chain.</summary>
    public const string Dimmed = "dimmed";

    /// <summary>No selection in this diagram: nothing is marked at all.</summary>
    public static SupplyChainTrace None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), false);

    private readonly IReadOnlyDictionary<string, string> _marks;

    private SupplyChainTrace(IReadOnlyDictionary<string, string> marks, bool active)
    {
        _marks = marks;
        IsActive = active;
    }

    /// <summary>Whether anything in this diagram is selected, so that everything is marked.</summary>
    public bool IsActive { get; }

    /// <summary>The marking of the entry with <paramref name="id"/>: empty when nothing is traced, dimmed when it is off the chain.</summary>
    public string Of(string id) => !IsActive ? "" : _marks.TryGetValue(id, out var mark) ? mark : Dimmed;

    /// <summary>The trace through <paramref name="selectedId"/> in a laid-out document, or <see cref="None"/> when it names nothing drawn.</summary>
    public static SupplyChainTrace Through(SupplyChainLayout layout, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (string.IsNullOrEmpty(selectedId))
        {
            return None;
        }

        var marks = new Dictionary<string, string>(StringComparer.Ordinal);
        List<string> upFrom;
        List<string> downFrom;

        if (layout.NodeBoxes.ContainsKey(selectedId))
        {
            upFrom = [selectedId];
            downFrom = [selectedId];
        }
        else if (layout.Flows.FirstOrDefault(f => f.Id == selectedId) is { } flow)
        {
            upFrom = [flow.From];
            downFrom = [flow.To];
            marks[flow.From] = Upstream;
            marks[flow.To] = Downstream;
        }
        else if (layout.GroupBoxes.ContainsKey(selectedId))
        {
            var members = layout.Nodes.Where(node => node.Group == selectedId).Select(node => node.Id).ToList();
            upFrom = members;
            downFrom = members;
        }
        else
        {
            return None;
        }

        var supplies = layout.Flows.ToLookup(flow => flow.To, StringComparer.Ordinal);
        var supplied = layout.Flows.ToLookup(flow => flow.From, StringComparer.Ordinal);

        Walk(upFrom, supplies, flow => flow.From, Upstream, marks);
        Walk(downFrom, supplied, flow => flow.To, Downstream, marks);

        // What the walk started from is the selection, whatever a walk marked it on the way round.
        if (layout.NodeBoxes.ContainsKey(selectedId) || layout.GroupBoxes.ContainsKey(selectedId))
        {
            foreach (var id in upFrom)
            {
                marks[id] = Selected;
            }
        }

        marks[selectedId] = Selected;

        foreach (var group in layout.Groups)
        {
            if (!marks.ContainsKey(group.Id) && layout.Nodes.Any(node => node.Group == group.Id && marks.ContainsKey(node.Id)))
            {
                marks[group.Id] = Related;
            }
        }

        return new SupplyChainTrace(marks, true);
    }

    /// <summary>
    /// Marks every flow and node reached from <paramref name="starts"/> along <paramref name="next"/>.
    /// A node reached both ways - goods that come back round - keeps the first marking it got.
    /// </summary>
    private static void Walk(
        IReadOnlyList<string> starts,
        ILookup<string, SupplyChainFlow> along,
        Func<SupplyChainFlow, string> next,
        string mark,
        Dictionary<string, string> marks)
    {
        var seen = new HashSet<string>(starts, StringComparer.Ordinal);
        var queue = new Queue<string>(starts);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var flow in along[id])
            {
                marks.TryAdd(flow.Id, mark);
                var reached = next(flow);
                marks.TryAdd(reached, mark);
                if (seen.Add(reached))
                {
                    queue.Enqueue(reached);
                }
            }
        }
    }
}
