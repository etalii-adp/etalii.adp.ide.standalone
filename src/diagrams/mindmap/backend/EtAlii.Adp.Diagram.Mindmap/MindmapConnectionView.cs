using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.Mindmap;

public sealed class MindmapConnectionView
{
    private readonly ConcurrentDictionary<string, byte> _folded = new(StringComparer.Ordinal);

    private MindmapConnectionView()
    {
    }

    /// <summary>The last reported viewport, or null before the first report - in which case everything is in view.</summary>
    public MindmapViewport? Viewport { get; set; }

    public bool IsFolded(string nodeId) => _folded.ContainsKey(nodeId);

    public bool IsFolded(MindmapNode node) => IsFolded(node.Id);

    private void SetFolded(string nodeId, bool folded)
    {
        if (folded)
        {
            _folded[nodeId] = 0;
        }
        else
        {
            _folded.TryRemove(nodeId, out _);
        }
    }

    public bool Toggle(string nodeId)
    {
        var now = !IsFolded(nodeId);
        SetFolded(nodeId, now);
        return now;
    }

    /// <summary>Every folded ancestor of <paramref name="node"/>, outermost first - what has to open for it to be visible (Requirement 10.5).</summary>
    public IReadOnlyList<MindmapNode> FoldedAncestorsOf(MindmapNode node)
    {
        var ancestors = new List<MindmapNode>();
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (IsFolded(current))
            {
                ancestors.Insert(0, current);
            }
        }

        return ancestors;
    }

    internal static MindmapConnectionView SeededFrom(MindmapDocument document)
    {
        var view = new MindmapConnectionView();
        foreach (var node in document.Nodes.Where(node => node.Folded))
        {
            view.SetFolded(node.Id, true);
        }

        return view;
    }
}
