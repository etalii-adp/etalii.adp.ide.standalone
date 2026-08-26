using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Judges a pipeline document, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 10.1).
/// </summary>
/// <remarks>
/// Text in, problems out. The rules themselves are a pure function over a parsed model, so this is
/// only the join: parse, and hand the model to <see cref="PipelineRuleSet"/>. A file that will not
/// parse is reported as one problem naming the line, since running graph rules over a model that
/// is empty only because the parse failed would bury that under a list of consequences.
/// </remarks>
public sealed class PipelineValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<PipelineValidator>();

    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "azure-pipeline.unparseable";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = Diagram.Definitions[0].Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PipelineModel model;
        try
        {
            model = PipelineParser.Parse(PipelineDocument.Parse(request.Document ?? ""));
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

        return ValueTask.FromResult(PipelineRuleSet.Judge(model));
    }
}
