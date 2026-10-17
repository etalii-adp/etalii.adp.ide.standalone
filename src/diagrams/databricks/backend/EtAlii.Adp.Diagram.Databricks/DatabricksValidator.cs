using EtAlii.Adp.Documents;

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
        var outcome = DatabricksModel.Outcome(document);
        if (!outcome.IsUsable)
        {
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not configuration that can be read: {outcome.Unreadable}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)outcome.Problems[0].Line + 1)),
            ]);
        }

        return ValueTask.FromResult(DatabricksRuleSet.Judge(outcome.Model));
    }
}
