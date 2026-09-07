using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Makes a query element selectable: resolves an <c>element_id</c> against the file named by the
/// enclosing level and verifies the id really is in it.
/// </summary>
public sealed class SparqlContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly ISparqlDocumentStore _documents;

    public SparqlContextSourceResolver(DiagramFileRouter router, ISparqlDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);

        _router = router;
        _documents = documents;
    }

    /// <inheritdoc />
    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    /// <inheritdoc />
    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != ServiceCollectionAddSparqlExtension.SparqlOrigin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a SPARQL query.");
        }

        var elementId = id.ElementId.Value;
        var text = SparqlSelection.Describe(_documents.GetOrLoad(bodyPath), elementId);
        if (text is null)
        {
            return Rejected("That element is no longer in this query.");
        }

        // The client's path is checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual([text], StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            [text],
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                bodyPath,
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                elementId,
                routed.Definition.Origin),
            new ContextLevelDetail { Element = new ElementDetail { Text = text } },
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>Nothing nests inside these elements for selection purposes.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    public IDisposable Track(
        ShortGuid watchId,
        string rootPath,
        ContextResolvedLevel level,
        Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = watchId;
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, SparqlDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: an edit in the text editor that
            // drops a variable clears the selection.
            var text = SparqlSelection.Describe(args.Entry, elementId);
            onChange(text is null ? null : [text]);
        }

        _documents.Changed += OnChanged;
        return new SparqlUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
