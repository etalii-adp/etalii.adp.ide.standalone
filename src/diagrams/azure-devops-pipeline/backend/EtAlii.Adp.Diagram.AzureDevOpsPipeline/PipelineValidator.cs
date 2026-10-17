using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

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
    /// <summary>The rule reported when the file is not YAML this can read.</summary>
    public const string UnparseableRuleId = "azure-pipeline.unparseable";

    /// <inheritdoc />
    public ToolOrigin Origin { get; } = Diagram.Pipeline.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract - Reason: IDiagramValidator.ValidateAsync is a public module seam and the record's non-nullable Document is a compile-time annotation the runtime does not enforce, so a caller that passes null (null!, or code built without nullable checks) gets an empty document here rather than a NullReferenceException; ProjectValidator, the one caller in this repository, always passes the text it read, so an empty file arrives as "", not null.
        var outcome = PipelineParser.Outcome(LineDocument.Parse(request.Document ?? ""));
        if (!outcome.IsUsable)
        {
            var problem = outcome.Problems[0];
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
            [
                new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"This is not YAML that can be read: {problem.Message}",
                    UnparseableRuleId,
                    new DiagramProblemLineLocation((uint)problem.Line + 1)),
            ]);
        }

        return ValueTask.FromResult(PipelineRuleSet.Judge(outcome.Model));
    }
}
