using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// One connection's live view of one mindmap. Holds nothing the document holds - it reads the
/// shared <see cref="MindmapDocument"/> - and adds only what is this connection's: its
/// viewport, and (through <see cref="MindmapViewState"/>) its folds. It turns each change to
/// the map, and each fold, into the deltas that carry it into this connection's view.
/// </summary>
internal sealed class MindmapSession : IDiagramSession
{
    private readonly ShortGuid _watchId;
    private readonly string _bodyPath;
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;
    private readonly MindmapElementMapper _mapper;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public MindmapSession(ShortGuid watchId, string bodyPath, IMindmapDocumentStore documents, MindmapViewState views, MindmapElementMapper mapper)
    {
        _watchId = watchId;
        _bodyPath = bodyPath;
        _documents = documents;
        _views = views;
        _mapper = mapper;
        _documents.Changed += OnDocumentChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var document = _documents.GetOrLoad(_bodyPath);
        var view = _views.For(_watchId, _bodyPath, document);
        var elements = _mapper.Visible(document, view, _viewport);
        return elements.Count == 0 ? [] : [new DiagramDelta.Add(elements)];
    }

    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        var document = _documents.Get(_bodyPath);
        if (document is null)
        {
            _viewport = viewport;
            return [];
        }

        var view = _views.For(_watchId, _bodyPath, document);
        var before = _mapper.Visible(document, view, _viewport).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        _viewport = viewport;
        var after = _mapper.Visible(document, view, _viewport);

        // What newly falls in view is added; what fell out is removed (Requirement 11.5).
        var removed = before.Except(after.Select(element => element.Id), StringComparer.Ordinal).ToArray();
        var appeared = after.Where(element => !before.Contains(element.Id)).ToArray();

        var deltas = new List<DiagramDelta>();
        if (appeared.Length > 0)
        {
            deltas.Add(new DiagramDelta.Add(appeared));
        }

        if (removed.Length > 0)
        {
            deltas.Add(new DiagramDelta.Remove(removed));
        }

        return deltas;
    }

    /// <summary>
    /// Toggles a fold and returns the group or ungroup delta for it, which the core service
    /// pushes (Requirement 11.4). The subtree stops or starts being delivered as part of it.
    /// </summary>
    public IReadOnlyList<DiagramDelta> ToggleFold(string nodeId)
    {
        var document = _documents.Get(_bodyPath);
        var node = document?.Find(nodeId);
        if (document is null || node is null || !node.HasChildren)
        {
            return [];
        }

        var view = _views.For(_watchId, _bodyPath, document);
        var descendantIds = node.Children.SelectMany(Descendants).Select(child => child.Id).ToArray();
        var nowFolded = view.Toggle(nodeId);

        var layout = _mapper.Layout(document, view);
        var groupElement = _mapper.ToElement(node, layout[nodeId]);
        if (nowFolded)
        {
            return [new DiagramDelta.Group(descendantIds, groupElement)];
        }

        var reappeared = descendantIds
            .Where(layout.ContainsKey)
            .Select(id => _mapper.ToElement(document.Find(id)!, layout[id]))
            .ToArray();
        return [new DiagramDelta.Ungroup(nodeId, reappeared)];
    }

    public ValueTask DisposeAsync()
    {
        _documents.Changed -= OnDocumentChanged;
        _views.Forget(_watchId, _bodyPath);
        return ValueTask.CompletedTask;
    }

    private void OnDocumentChanged(object? sender, MindmapChangedEventArgs args)
    {
        if (!string.Equals(args.BodyPath, _bodyPath, StringComparison.OrdinalIgnoreCase) || Changed is null)
        {
            return;
        }

        var document = _documents.Get(_bodyPath);
        if (document is null)
        {
            return;
        }

        var view = _views.For(_watchId, _bodyPath, document);
        var deltas = args.Change switch
        {
            MindmapChange.NodeUpdated updated when document.Find(updated.NodeId) is { } node && _mapper.Layout(document, view).TryGetValue(node.Id, out var box) =>
                (IReadOnlyList<DiagramDelta>)[new DiagramDelta.Add([_mapper.ToElement(node, box)])],

            // A structure change relays out what is now visible, plus removes for what went -
            // simplest correct answer, and a mindmap edit is not a hot path (Requirement 11.3).
            MindmapChange.StructureChanged structure => Relayout(document, view, structure.RemovedNodeIds),
            MindmapChange.Reloaded => Relayout(document, view, []),
            _ => [],
        };

        if (deltas.Count > 0)
        {
            Changed.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }

    private IReadOnlyList<DiagramDelta> Relayout(MindmapDocument document, MindmapViewState.ConnectionView view, IReadOnlyList<string> removedIds)
    {
        var visible = _mapper.Visible(document, view, _viewport);
        var deltas = new List<DiagramDelta>();
        if (visible.Count > 0)
        {
            deltas.Add(new DiagramDelta.Add(visible));
        }

        if (removedIds.Count > 0)
        {
            deltas.Add(new DiagramDelta.Remove(removedIds));
        }

        return deltas;
    }

    private static IEnumerable<MindmapNode> Descendants(MindmapNode node)
    {
        yield return node;
        foreach (var descendant in node.Children.SelectMany(Descendants))
        {
            yield return descendant;
        }
    }
}
