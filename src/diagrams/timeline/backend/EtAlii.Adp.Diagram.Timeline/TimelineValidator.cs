using Serilog;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Judges a timeline document, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 12.1).
/// </summary>
/// <remarks>
/// Text in, problems out: ask <see cref="TimelineYamlSyntax"/> whether it is YAML, read it through the
/// binding, and hand the body to <see cref="TimelineDefinition.Problems"/>. A file that is not YAML is
/// reported as one <b>error</b> naming the line - the definition's <c>std.unparseable</c>, with the YAML
/// reader's message as its reason, and the same message the unavailable state shows, so the panel and
/// the canvas never disagree (Requirement 12.3) - because running rules over a model that is empty
/// only because the parse failed would bury that one fact under a list of consequences.
/// </remarks>
public sealed class TimelineValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<TimelineValidator>();

    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "timeline.unparseable";

    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (TimelineYamlSyntax.Check(request.Document) is { } refusal)
        {
            _logger.Debug("{BaseName} does not parse at line {Line}: {Reason}", request.BaseName, refusal.Line, refusal.Message);
            return ValueTask.FromResult(TimelineDefinition.Unparseable(refusal.Message, refusal.Line));
        }

        return ValueTask.FromResult(TimelineDefinition.Problems(TimelineBody.Parse(request.Document)));
    }
}
