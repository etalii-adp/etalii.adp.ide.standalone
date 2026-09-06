using EtAlii.Adp.Common;
using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Judges one of the family's documents, through the seam every diagram type's rules reach the
/// Errors and Warnings panel by (databricks-diagrams Requirement 12).
/// </summary>
/// <remarks>
/// Text in, problems out. The rules are a pure function over a parsed entry, so this is only the
/// join: parse, and hand the entry to <see cref="DatabricksRuleSet"/>. A file that will not
/// parse is reported as one problem naming the line, since running the rules over models that
/// are empty only because the parse failed would bury that under a list of consequences. One
/// instance per MIME type, because the seam resolves by origin - the rules are the family's and
/// do not care which of its three types asked.
/// </remarks>
public sealed class DatabricksValidator(DiagramOrigin origin) : IDiagramValidator
{
    private static readonly ILogger _logger = Log.ForContext<DatabricksValidator>();

    /// <summary>The rule reported when the file is not YAML (or JSON) this can read.</summary>
    public const string UnparseableRuleId = "databricks.unparseable";

    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        // Reason: Can still be null if the document is empty.
        var document = DatabricksDocument.Parse(request.Document ?? "");
        DatabricksDocumentEntry entry;
        try
        {
            var root = DatabricksYaml.Root(document);
            entry = new DatabricksDocumentEntry(
                document,
                BundleParser.Parse(root, document),
                JobParser.Parse(root, document),
                PipelineParser.Parse(root, document),
                "",
                0);
        }
        catch (YamlException exception)
        {
            var line = (uint)Math.Max(exception.Start.Line, 1);
            _logger.Debug(exception, "{BaseName} does not parse at line {Line}", request.BaseName, line);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not configuration that can be read: {exception.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation(line)),
            ]);
        }

        return ValueTask.FromResult(DatabricksRuleSet.Judge(entry));
    }
}
