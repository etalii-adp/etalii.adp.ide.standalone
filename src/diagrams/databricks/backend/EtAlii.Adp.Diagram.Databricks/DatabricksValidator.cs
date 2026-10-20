using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Judges one of the family's documents, through the seam every diagram type's rules reach the
/// Errors and Warnings panel by (databricks-diagrams Requirement 12).
/// </summary>
/// <remarks>
/// Text in, problems out. The findings are the file's, derived from the three definitions and joined in the tool's
/// order by <see cref="DatabricksFindings"/>; this is only the parse and the mapping onto the panel's problems, each
/// at the line the finding names. A file that will not parse is reported as one problem naming the line, since
/// running the rules over models that are empty only because the parse failed would bury that under a list of
/// consequences. One instance per MIME type, because the seam resolves by origin - the findings are the file's and do
/// not depend on which of its three types asked (databricks-disl-fbl Q3).
/// </remarks>
public sealed class DatabricksValidator(ToolOrigin origin) : IDiagramValidator
{
    /// <summary>The rule reported when the file is not YAML (or JSON) this can read.</summary>
    public const string UnparseableRuleId = "databricks.unparseable";

    /// <inheritdoc />
    public ToolOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - Reason: IDiagramValidator.ValidateAsync is a public module seam and the record's non-nullable Document is a compile-time annotation the runtime does not enforce, so a caller that passes null (null!, or code built without nullable checks) gets an empty document here rather than a NullReferenceException; ProjectValidator, the one caller in this repository, always passes the text it read, so an empty file arrives as "", not null.
        var document = LineDocument.Parse(request.Document ?? "");
        var entry = new DocumentEntry<LineDocument, DatabricksModel>(document, DatabricksModel.Outcome(document));
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([.. DatabricksFindings.Of(entry, Path.GetFileName(request.BodyPath)).Select(Problem)]);
    }

    /// <summary>A finding as the panel lists it: its severity, sentence and code, at its line.</summary>
    private static DiagramProblem Problem(DislFinding finding) =>
        new(
            DislDiagramDefinition.SeverityOf(finding),
            finding.Message,
            finding.Code,
            finding.Line is { } line ? new DiagramProblemLineLocation((uint)line) : null);
}
