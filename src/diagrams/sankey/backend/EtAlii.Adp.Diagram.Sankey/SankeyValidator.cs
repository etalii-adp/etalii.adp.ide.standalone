using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>One thing wrong with a document, with the zero-based line it was found on.</summary>
public sealed record SankeyBreach(string RuleId, string Message, int Line, bool IsWarning);

/// <summary>The rules a <c>.skv</c> document is checked against, each with its id.</summary>
public static class SankeyRuleIds
{
    public const string UnreadableEntry = "sankey.unreadable";
    public const string MissingId = "sankey.missing-id";
    public const string DuplicateId = "sankey.duplicate-id";
    public const string DanglingFlow = "sankey.dangling-flow";
    public const string SelfFlow = "sankey.self-flow";
    public const string MissingValue = "sankey.missing-value";
    public const string NegativeValue = "sankey.negative-value";
    public const string UnknownColor = "sankey.unknown-color";
    public const string BackwardFlow = "sankey.backward-flow";
}

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// <b>What is lost is an error, what is merely untidy a warning.</b> A node without an id, a flow to
/// a node that is not there, a duplicate or a flow to itself are not drawn; a flow without a value,
/// an unknown colour or a flow running backwards still draw what the author meant.
/// </remarks>
public sealed class SankeyValidator : IDiagramValidator
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = Validate(SankeyParser.Parse(LineDocument.Parse(request.Document))).Select(ToProblem).ToList();
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>Everything wrong with a model already parsed.</summary>
    public static IReadOnlyList<SankeyBreach> Validate(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SankeyBreach> breaches =
        [
            .. model.Problems.Select(problem => new SankeyBreach(SankeyRuleIds.UnreadableEntry, problem.Message, problem.Line, true)),
            .. model.Nodes
                .Where(node => node.Id.Length == 0)
                .Select(node => new SankeyBreach(SankeyRuleIds.MissingId, "A node has no id and is not drawn.", node.Range.Start, false)),
        ];

        var entries = model.Nodes.Where(node => node.Id.Length > 0).Select(node => (node.Id, What: "node", node.Range.Start))
            .Concat(model.Flows.Select(flow => (flow.Id, What: "flow", flow.Range.Start)))
            .ToList();

        breaches.AddRange(entries
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Skip(1))
            .Select(entry => new SankeyBreach(
                SankeyRuleIds.DuplicateId,
                entry.What == "flow" && entry.Id.Contains("->", StringComparison.Ordinal)
                    ? $"A flow from `{entry.Id.Replace("->", "` to `", StringComparison.Ordinal)}` is written twice; only the first is drawn - add their values, or give each an id."
                    : $"The id `{entry.Id}` is used more than once; only its first entry is drawn.",
                entry.Start,
                false)));

        var nodes = model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (flow.From.Length > 0 && flow.From == flow.To)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.SelfFlow, $"The flow from `{flow.From}` runs to itself and is not drawn.", flow.Range.Start, false));
                continue;
            }

            var missing = new[] { flow.From, flow.To }.Where(end => !nodes.Contains(end)).ToList();
            foreach (var end in missing)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.DanglingFlow,
                    end.Length == 0 ? "A flow is missing an end and is not drawn." : $"A flow names `{end}`, which is not a node here; it is not drawn.",
                    flow.Range.Start, false));
            }

            if (missing.Count > 0)
            {
                continue;
            }

            if (flow.Value is null)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.MissingValue, $"The flow from `{flow.From}` to `{flow.To}` states no value, so it is drawn as a hairline.", flow.Range.Start, true));
            }
            else if (flow.Value < 0)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.NegativeValue, $"The flow from `{flow.From}` to `{flow.To}` has a negative value; it is drawn as zero.", flow.Range.Start, true));
            }

            if (!SankeyColors.IsKnown(flow.Color))
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.UnknownColor, UnknownColor(flow.Color), flow.Range.Start, true));
            }
        }

        breaches.AddRange(model.Nodes
            .Where(node => !SankeyColors.IsKnown(node.Color))
            .Select(node => new SankeyBreach(SankeyRuleIds.UnknownColor, UnknownColor(node.Color), node.Range.Start, true)));

        var layout = SankeyLayout.Of(model);
        breaches.AddRange(layout.Flows
            .Where(flow => layout.Bands[flow.Id].Backward)
            .Select(flow => new SankeyBreach(
                SankeyRuleIds.BackwardFlow,
                $"The flow from `{flow.From}` to `{flow.To}` runs back to a column at or before its own, so it loops round instead of reading left to right.",
                flow.Range.Start,
                true)));

        return [.. breaches.OrderBy(breach => breach.Line)];
    }

    private static string UnknownColor(string color) =>
        $"`{color}` is not a colour this diagram knows - one of {string.Join(", ", SankeyColors.Palette)}, or #rrggbb; it is drawn as if it stated none.";

    private static DiagramProblem ToProblem(SankeyBreach breach) => new(
        breach.IsWarning ? DiagramProblemSeverity.Warning : DiagramProblemSeverity.Error,
        breach.Message,
        breach.RuleId,
        new DiagramProblemLineLocation((uint)Math.Max(breach.Line, 0) + 1));
}
