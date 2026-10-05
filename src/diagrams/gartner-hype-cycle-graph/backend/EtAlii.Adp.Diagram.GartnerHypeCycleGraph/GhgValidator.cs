using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it - and the
/// seam that carries each breach to the Errors and Warnings panel (Requirement 2.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and they are different questions</b>, as in FDG: the parser answers what it could
/// read, the rule set what the notation forbids. This puts the two together.
/// </para>
/// <para>
/// <b>Registered as the module's <see cref="IDiagramValidator"/></b>, which is where this module
/// parts from FDG: FDG's validator is reachable from its tests only, while Requirement 2.4 here asks
/// for every breach in the panel.
/// </para>
/// </remarks>
public sealed class GhgValidator : IDiagramValidator
{
    /// <summary>Everything wrong with the document, parse problems and rule breaches together.</summary>
    public static IReadOnlyList<GhgBreach> Validate(GhgBody document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return GhgRuleSet.Breaches(GhgParser.Parse(document));
    }

    /// <summary>Everything wrong with a model already parsed.</summary>
    public static IReadOnlyList<GhgBreach> Validate(GhgModel model) => GhgRuleSet.Breaches(model);

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems =
        [
            .. Validate(GhgBody.Parse(request.Document)).Select(breach => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                breach.Message,
                breach.RuleId,
                new DiagramProblemLineLocation((uint)(breach.Line + 1)))),
        ];

        return ValueTask.FromResult(problems);
    }
}
