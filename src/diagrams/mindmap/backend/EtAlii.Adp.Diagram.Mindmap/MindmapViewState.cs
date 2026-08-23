using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What one connection sees of one map that is not the map itself: which branches it has
/// folded, and the viewport it last reported. Held per <c>(watchId, bodyPath)</c> and never
/// written to the <c>.mm</c> file, so two viewers fold independently and a fold leaves
/// nothing on the history (Requirements 9.4, 9.6).
/// </summary>
public sealed class MindmapViewState
{
    private readonly ConcurrentDictionary<(ShortGuid WatchId, string BodyPath), ConnectionView> _views = new();

    /// <summary>The view for a connection on a map, created on first ask - seeded from the file's own FOLDED attributes (Requirement 9.3).</summary>
    public ConnectionView For(ShortGuid watchId, string bodyPath, MindmapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _views.GetOrAdd((watchId, bodyPath.ToUpperInvariant()), _ => ConnectionView.SeededFrom(document));
    }

    /// <summary>The view if the connection has one on this map; null otherwise, and nothing is created.</summary>
    public ConnectionView? Find(ShortGuid watchId, string bodyPath) =>
        _views.TryGetValue((watchId, bodyPath.ToUpperInvariant()), out var view) ? view : null;

    /// <summary>Drops everything a connection held, when its stream ends.</summary>
    public void Forget(ShortGuid watchId)
    {
        foreach (var key in _views.Keys.Where(key => key.WatchId == watchId).ToArray())
        {
            _views.TryRemove(key, out _);
        }
    }

    /// <summary>Drops one connection's view of one map, when it closes that diagram.</summary>
    public void Forget(ShortGuid watchId, string bodyPath) =>
        _views.TryRemove((watchId, bodyPath.ToUpperInvariant()), out _);

    public sealed class ConnectionView
    {
        private readonly ConcurrentDictionary<string, byte> _folded = new(StringComparer.Ordinal);

        private ConnectionView()
        {
        }

        /// <summary>The last reported viewport, or null before the first report - in which case everything is in view.</summary>
        public MindmapViewport? Viewport { get; set; }

        public bool IsFolded(string nodeId) => _folded.ContainsKey(nodeId);

        public bool IsFolded(MindmapNode node) => IsFolded(node.Id);

        public void SetFolded(string nodeId, bool folded)
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

        internal static ConnectionView SeededFrom(MindmapDocument document)
        {
            var view = new ConnectionView();
            foreach (var node in document.Nodes.Where(node => node.Folded))
            {
                view.SetFolded(node.Id, true);
            }

            return view;
        }
    }
}

/// <summary>A connection's reported viewport: the visible rectangle in canvas units (Requirement 11.5).</summary>
public readonly record struct MindmapViewport(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Intersects(MindmapBox box) =>
        box.Right >= MinX && box.X <= MaxX && box.Bottom >= MinY && box.Y <= MaxY;
}
