using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Judges a pipeline document, through the seam every diagram type's rules reach the Errors and
/// Warnings panel by (Requirement 10.1).
/// </summary>
/// <remarks>
/// Text in, problems out: the findings of the bundled definition (<see cref="PipelineDefinition.Findings"/>)
/// over the model the parser reads, without following a template, as this has always judged. A file
/// that will not parse is the reader's one finding naming the line, since running graph rules over a
/// model that is empty only because the parse failed would bury that under a list of consequences.
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
        IReadOnlyList<DislReaderFinding> reader = outcome.IsUsable
            ? []
            : [DislReaderFinding.NotParsed(outcome.Problems[0].Message, outcome.Problems[0].Line + 1)];

        IReadOnlyList<DiagramProblem> problems =
        [
            .. PipelineDefinition.Findings(outcome.Model, reader).Select(finding => new DiagramProblem(
                DislDiagramDefinition.SeverityOf(finding),
                finding.Message,
                finding.Code,
                Location(finding))),
        ];
        return ValueTask.FromResult(problems);
    }

    /// <summary>Where a finding is: the element it names first, else the line the reader names, else nowhere.</summary>
    private static DiagramProblemLocation? Location(DislFinding finding) =>
        finding.ElementIds.Count > 0
            ? new DiagramProblemElementLocation(finding.ElementIds[0])
            : finding.Line is { } line
                ? new DiagramProblemLineLocation((uint)line)
                : null;
}
