using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Judges a dependency graph document, through the seam every diagram type's rules reach the
/// Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// Text in, problems out: parse, and hand the model to <see cref="DependencyGraphRuleSet"/>. A
/// file that will not parse is reported as one <b>error</b> naming the line - the same message
/// the unavailable state shows, so the panel and the canvas never disagree - because running
/// rules over a model that is empty only because the parse failed would bury that one fact under
/// a list of consequences.
/// </remarks>
public sealed class DependencyGraphValidator : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphValidator>();

    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "dependencies.unparseable";

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DependencyGraphModel model;
        try
        {
            model = DependencyGraphParser.Parse(LineDocument.Parse(request.Document));
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

        return ValueTask.FromResult(DependencyGraphRuleSet.Judge(model));
    }
}
