using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Judges a timeline document, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 12.1).
/// </summary>
/// <remarks>
/// Text in, problems out: parse, and hand the model to <see cref="TimelineRuleSet"/>. A file
/// that will not parse is reported as one <b>error</b> naming the line - the same message the
/// unavailable state shows, so the panel and the canvas never disagree (Requirement 12.3) -
/// because running rules over a model that is empty only because the parse failed would bury
/// that one fact under a list of consequences.
/// </remarks>
public sealed class TimelineValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<TimelineValidator>();

    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "timeline.unparseable";

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Timeline.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        TimelineModel model;
        try
        {
            model = TimelineParser.Parse(LineDocument.Parse(request.Document));
        }
        catch (YamlException exception)
        {
            var line = (uint)Math.Max(exception.Start.Line, 1);
            _logger.Debug(exception, "{BaseName} does not parse at line {Line}", request.BaseName, line);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not YAML that can be read: {exception.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation(line)),
            ]);
        }

        return ValueTask.FromResult(TimelineRuleSet.Judge(model));
    }
}
