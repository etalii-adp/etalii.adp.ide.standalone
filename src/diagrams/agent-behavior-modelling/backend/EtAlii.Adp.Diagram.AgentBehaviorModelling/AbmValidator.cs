using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Reads a behavior model and reports what an agent following it would trip over, without ever
/// refusing to read it, through the seam every diagram type's rules reach the Errors and Warnings
/// panel by.
/// </summary>
public sealed class AbmValidator : IDiagramValidator
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = Validate(LineDocument.Parse(request.Document)).Select(ToProblem).ToList();
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>Everything wrong with the document.</summary>
    public static IReadOnlyList<AbmBreach> Validate(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return AbmRuleSet.Breaches(AbmParser.Parse(document));
    }

    /// <summary>One breach as the panel reads it; the location is the line, one-based as the panel counts.</summary>
    private static DiagramProblem ToProblem(AbmBreach breach) => new(
        breach.IsInformation ? DiagramProblemSeverity.Info : breach.IsError ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning,
        breach.Message,
        breach.RuleId,
        new DiagramProblemLineLocation((uint)Math.Max(breach.Line, 0) + 1));
}
