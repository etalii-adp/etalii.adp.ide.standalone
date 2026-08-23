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

    /// <summary>
    /// Raised when a connection collapses or expands a branch, so the session holding that
    /// connection's stream can push the matching group or ungroup delta. Without this seam a
    /// toggle only changes state and no client ever hears of it - the provider that executes
    /// the action and the session that owns the stream never meet otherwise.
    /// </summary>
    public event EventHandler<MindmapFoldToggledEventArgs>? FoldToggled;

    /// <summary>The view for a connection on a map, created on first ask - seeded from the file's own FOLDED attributes (Requirement 9.3).</summary>
    public ConnectionView For(ShortGuid watchId, string bodyPath, MindmapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _views.GetOrAdd((watchId, bodyPath.ToUpperInvariant()), _ => ConnectionView.SeededFrom(document));
    }

    /// <summary>
    /// Collapses or expands one branch for one connection and announces it. The one route a
    /// toggle is allowed to take: state and notification stay together, so a session cannot
    /// miss a fold the way it would if callers toggled the view directly.
    /// </summary>
    public bool Toggle(ShortGuid watchId, string bodyPath, MindmapDocument document, string nodeId)
    {
        var nowFolded = For(watchId, bodyPath, document).Toggle(nodeId);
        FoldToggled?.Invoke(this, new MindmapFoldToggledEventArgs(watchId, bodyPath, nodeId, nowFolded));
        return nowFolded;
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

/// <summary>One connection collapsed or expanded one branch.</summary>
public sealed class MindmapFoldToggledEventArgs(ShortGuid watchId, string bodyPath, string nodeId, bool folded) : EventArgs
{
    public ShortGuid WatchId { get; } = watchId;

    public string BodyPath { get; } = bodyPath;

    public string NodeId { get; } = nodeId;

    public bool Folded { get; } = folded;
}
