using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Makes a pipeline element selectable: resolves an <c>element_id</c> against the file named by
/// the enclosing selection level, verifies the element really is in it, and fills in the detail a
/// consumer shows.
/// </summary>
/// <remarks>
/// Registering this is the whole of it - the context service is untouched (Requirement 12.2). The
/// detail is resolved here rather than looked up later, so the property grid shows what is
/// selected without a second round trip (Requirement 12.3).
/// </remarks>
public sealed class PipelineContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IPipelineDocumentStore _documents;

    /// <summary>Creates the resolver.</summary>
    public PipelineContextSourceResolver(DiagramFileRouter router, IPipelineDocumentStore documents)
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
        cancellationToken.ThrowIfCancellationRequested();

        // An element is only ever selected inside a diagram: with no file level above it there is
        // nothing to verify it against.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A pipeline element must be selected within its pipeline.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin.Vendor != Diagram.Pipeline.Origin.Vendor)
        {
            return Rejected("The selected file is not an Azure pipeline.");
        }

        var bodyPath = routed.BodyPath!;
        var entry = _documents.GetOrLoad(rootPath, bodyPath);
        if (!entry.IsUsable)
        {
            // Nothing can be selected in a file that does not parse, and saying so is better than
            // rejecting an element the user can see on a canvas that has not caught up yet.
            return Rejected($"{System.IO.Path.GetFileName(bodyPath)} does not parse, so nothing in it can be selected.");
        }

        var elementId = id.ElementId.Value;
        var found = Find(entry.Model, elementId);
        if (found is null)
        {
            return Rejected("Unknown element.");
        }

        // The path is the element's chain of names within the pipeline. The client's version is
        // checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(found.Path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            found.Path,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                bodyPath,
                IsContainer: found.HasChildren,
                SourceId: default,
                rootPath,
                watchId,
                elementId,
                routed.Definition.Origin),
            new ContextLevelDetail
            {
                Element = new ElementDetail
                {
                    Text = found.Text,
                    HasChildren = found.HasChildren,
                    Folded = false,
                    // An element from a template is a link to somewhere else as much as a thing in
                    // its own right: its text lives in another file, and that is where editing it
                    // has to happen (Requirement 5.4).
                    Linked = found.IsFromTemplate,
                },
            },
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>
    /// A stage holds jobs and a job holds steps, so both nest - and they nest by containment: a
    /// job's path is read relative to its stage, which is what makes "Build / Compile" a path
    /// rather than two unrelated names. A step holds nothing.
    /// </summary>
    public ContextNesting NestingOf(ContextResolvedLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return level.Target.IsContainer ? ContextNesting.Contained : ContextNesting.NotNestable;
    }

    /// <summary>
    /// Re-resolves after every change to the pipeline: a rename changes the path, a deleted stage
    /// clears the selection. One document may back several open views, so a change through any of
    /// them re-resolves this one.
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, PipelineDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            onChange(Find(args.Model, elementId)?.Path);
        }

        _documents.Changed += OnChanged;
        return new PipelineChangeUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>
    /// The element an id names, whatever kind it is.
    /// </summary>
    /// <remarks>
    /// Searched rather than indexed because the model is small and rebuilt on every change; an
    /// index would be a second thing to keep in step with it for no measurable gain.
    /// </remarks>
    private static PipelineSelectableElement? Find(PipelineModel model, string elementId)
    {
        foreach (var stage in model.Stages)
        {
            if (string.Equals(stage.Id, elementId, StringComparison.Ordinal))
            {
                return new PipelineSelectableElement(
                    [stage.Label],
                    stage.Label,
                    stage.Jobs.Count > 0,
                    stage.IsFromTemplate);
            }

            foreach (var job in stage.Jobs)
            {
                if (string.Equals(job.Id, elementId, StringComparison.Ordinal))
                {
                    return new PipelineSelectableElement(
                        [stage.Label, job.Label],
                        job.Label,
                        job.Steps.Count > 0,
                        job.IsFromTemplate);
                }

                foreach (var step in job.Steps)
                {
                    if (string.Equals(step.Id, elementId, StringComparison.Ordinal))
                    {
                        return new PipelineSelectableElement(
                            [stage.Label, job.Label, step.Label],
                            step.Label,
                            HasChildren: false,
                            step.IsFromTemplate);
                    }
                }
            }
        }

        return null;
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
