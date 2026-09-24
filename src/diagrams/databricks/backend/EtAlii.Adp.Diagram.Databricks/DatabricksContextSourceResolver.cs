using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Makes a Databricks element selectable: resolves an <c>element_id</c> against the file named
/// by the enclosing level and verifies the id really is in it - once for the whole family,
/// because which of the three types a file is does not change what an element is.
/// </summary>
/// <remarks>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone
/// whether the element is one of its own - only the file above it says that. So this refuses a
/// file that does not route to this family, and the selection resolver moves on.
/// </remarks>
public sealed class DatabricksContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IDatabricksDocumentStore _documents;

    public DatabricksContextSourceResolver(DiagramFileRouter router, IDatabricksDocumentStore documents)
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
            routed.Definition.Origin.Vendor != "databricks" ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a Databricks configuration.");
        }

        var elementId = id.ElementId.Value;

        // A placement or a finished relation gesture names the element about to exist - it
        // resolves like any element, to a target the action provider reads the gesture back out
        // of; it lives for one ExecuteAction and is never selected, tracked or written anywhere.
        if (DatabricksNewPlacement.TryParse(elementId, out _, out _) ||
            DatabricksRelationGesture.TryParse(elementId, out _, out _))
        {
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                source,
                id,
                ["New element"],
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
                new ContextLevelDetail { Element = new ElementDetail { Text = "New element" } },
                this)));
        }

        var text = DatabricksSelection.Describe(_documents.GetOrLoad(bodyPath), elementId);
        if (text is null)
        {
            return Rejected("That element is no longer in this file.");
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

        void OnChanged(object? sender, DatabricksDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a removal clears the selection.
            var text = DatabricksSelection.Describe(args.Entry, elementId);
            onChange(text is null ? null : [text]);
        }

        _documents.Changed += OnChanged;
        return new DatabricksUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
