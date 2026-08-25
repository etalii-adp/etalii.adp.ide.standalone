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
    private readonly Backend.IHistoryStack _history;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public MindmapSession(
        ShortGuid watchId,
        string bodyPath,
        IMindmapDocumentStore documents,
        MindmapViewState views,
        MindmapElementMapper mapper,
        Backend.IHistoryStack history)
    {
        _watchId = watchId;
        _bodyPath = bodyPath;
        _documents = documents;
        _views = views;
        _mapper = mapper;
        _history = history;
        _documents.Changed += OnDocumentChanged;
        // The fold action executes in the context provider, far from this stream; the view
        // state's event is what carries it here so the group/ungroup delta gets pushed.
        _views.FoldToggled += OnFoldToggled;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var document = _documents.GetOrLoad(_bodyPath);
        var view = _views.For(_watchId, _bodyPath, document);
        var elements = _mapper.Visible(document, view, _viewport);
        return elements.Count == 0 ? [] : [new DiagramAddDelta(elements)];
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
            deltas.Add(new DiagramAddDelta(appeared));
        }

        if (removed.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(removed));
        }

        return deltas;
    }

    /// <summary>
    /// Moves an element under a new parent - a drag on the canvas. Dispatched as a command
    /// through the project's history, so the move is one undo away like every other edit;
    /// the resulting document change reaches every open view through the store's own event.
    /// </summary>
    public async Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken)
    {
        var result = await _history.ExecuteAsync(new MoveNodeCommand(_bodyPath, elementId, newParentId, index), cancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// This connection collapsed or expanded a branch: push the group or ungroup delta that
    /// carries it into the view (Requirement 11.4). The subtree stops or starts being
    /// delivered as part of it. Another connection's fold is not this stream's business.
    /// </summary>
    private void OnFoldToggled(object? sender, MindmapFoldToggledEventArgs args)
    {
        if (args.WatchId != _watchId
            || !string.Equals(args.BodyPath, _bodyPath, StringComparison.OrdinalIgnoreCase)
            || Changed is null)
        {
            return;
        }

        var document = _documents.Get(_bodyPath);
        var node = document?.Find(args.NodeId);
        if (document is null || node is null || !node.HasChildren)
        {
            return;
        }

        var view = _views.For(_watchId, _bodyPath, document);
        var descendantIds = node.Children.SelectMany(Descendants).Select(child => child.Id).ToArray();

        var layout = _mapper.Layout(document, view);
        var groupElement = _mapper.ToElement(node, layout[args.NodeId]);
        // Collapsing or expanding a branch moves everything that stays visible - the freed or
        // reclaimed room shifts the surviving subtrees - so the group/ungroup travels with an
        // upsert of the whole visible set, which is what puts every survivor where the new
        // layout wants it. Without it the hidden branch goes but nothing else budges.
        var repositioned = new DiagramAddDelta(_mapper.Visible(document, view, _viewport));
        if (args.Folded)
        {
            Changed.Invoke(this, new DiagramDeltasEventArgs([new DiagramGroupDelta(descendantIds, groupElement), repositioned]));
            return;
        }

        var reappeared = descendantIds
            .Where(layout.ContainsKey)
            .Select(id => _mapper.ToElement(document.Find(id)!, layout[id]))
            .ToArray();
        Changed.Invoke(this, new DiagramDeltasEventArgs([new DiagramUngroupDelta(args.NodeId, reappeared), repositioned]));
    }

    public ValueTask DisposeAsync()
    {
        _views.FoldToggled -= OnFoldToggled;
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
            MindmapNodeUpdated updated when document.Find(updated.NodeId) is { } node && _mapper.Layout(document, view).TryGetValue(node.Id, out var box) =>
                (IReadOnlyList<DiagramDelta>)[new DiagramAddDelta([_mapper.ToElement(node, box)])],

            // A structure change relays out what is now visible, plus removes for what went -
            // simplest correct answer, and a mindmap edit is not a hot path (Requirement 11.3).
            MindmapStructureChanged structure => Relayout(document, view, structure.RemovedNodeIds),
            MindmapReloaded => Relayout(document, view, []),
            _ => [],
        };

        if (deltas.Count > 0)
        {
            Changed.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }

    private IReadOnlyList<DiagramDelta> Relayout(MindmapDocument document, MindmapConnectionView view, IReadOnlyList<string> removedIds)
    {
        var visible = _mapper.Visible(document, view, _viewport);
        var deltas = new List<DiagramDelta>();
        if (visible.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(visible));
        }

        if (removedIds.Count > 0)
        {
            deltas.Add(new DiagramRemoveDelta(removedIds));
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
