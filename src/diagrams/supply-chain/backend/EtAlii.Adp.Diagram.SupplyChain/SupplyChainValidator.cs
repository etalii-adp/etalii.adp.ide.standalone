using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>One thing wrong with a document, with the zero-based line it was found on.</summary>
public sealed record SupplyChainBreach(string RuleId, string Message, int Line, bool IsWarning);

/// <summary>The rules a <c>.supply</c> document is checked against, each with its id.</summary>
public static class SupplyChainRuleIds
{
    public const string UnreadableEntry = "supply-chain.unreadable";
    public const string MissingId = "supply-chain.missing-id";
    public const string DuplicateId = "supply-chain.duplicate-id";
    public const string DanglingFlow = "supply-chain.dangling-flow";
    public const string SelfFlow = "supply-chain.self-flow";
    public const string UnknownGroup = "supply-chain.unknown-group";
    public const string EmptyGroup = "supply-chain.empty-group";
}

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// <b>What is lost is an error, what is merely untidy a warning.</b> A flow to a node that is not
/// there, a duplicate id or a self-flow are not drawn; an unreadable line, an unknown group or an
/// empty one still draw everything the author meant.
/// </remarks>
public sealed class SupplyChainValidator : IDiagramValidator
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var problems = Validate(SupplyChainParser.Parse(LineDocument.Parse(request.Document))).Select(ToProblem).ToList();
        return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(problems);
    }

    /// <summary>Everything wrong with a model already parsed.</summary>
    public static IReadOnlyList<SupplyChainBreach> Validate(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SupplyChainBreach> breaches =
        [
            .. model.Problems.Select(problem => new SupplyChainBreach(SupplyChainRuleIds.UnreadableEntry, problem.Message, problem.Line, true)),
        ];

        var entries = model.Groups.Select(group => (group.Id, What: "group", group.Range.Start))
            .Concat(model.Nodes.Select(node => (node.Id, What: "node", node.Range.Start)))
            .Concat(model.Flows.Select(flow => (flow.Id, What: "flow", flow.Range.Start)))
            .ToList();

        breaches.AddRange(entries
            .Where(entry => entry.Id.Length == 0)
            .Select(entry => new SupplyChainBreach(SupplyChainRuleIds.MissingId, $"A {entry.What} has no id and is not drawn.", entry.Start, false)));

        breaches.AddRange(entries
            .Where(entry => entry.Id.Length > 0)
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Skip(1))
            .Select(entry => new SupplyChainBreach(SupplyChainRuleIds.DuplicateId, $"The id `{entry.Id}` is used more than once; only its first entry is drawn.", entry.Start, false)));

        var nodes = model.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (flow.From.Length > 0 && flow.From == flow.To)
            {
                breaches.Add(new(SupplyChainRuleIds.SelfFlow, $"The flow `{flow.Id}` runs from `{flow.From}` to itself and is not drawn.", flow.Range.Start, false));
                continue;
            }

            foreach (var end in new[] { flow.From, flow.To }.Where(end => !nodes.Contains(end)))
            {
                breaches.Add(new(SupplyChainRuleIds.DanglingFlow,
                    end.Length == 0 ? $"The flow `{flow.Id}` is missing an end and is not drawn." : $"The flow `{flow.Id}` names `{end}`, which is not a node here; it is not drawn.",
                    flow.Range.Start, false));
            }
        }

        var groups = model.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        breaches.AddRange(model.Nodes
            .Where(node => node.Group.Length > 0 && !groups.Contains(node.Group))
            .Select(node => new SupplyChainBreach(SupplyChainRuleIds.UnknownGroup, $"The node `{node.Id}` is in `{node.Group}`, which is not a group here; it is drawn outside every group.", node.Range.Start, true)));

        breaches.AddRange(model.Groups
            .Where(group => group.Id.Length > 0 && !group.IsPlaced && model.Nodes.All(node => node.Group != group.Id))
            .Select(group => new SupplyChainBreach(SupplyChainRuleIds.EmptyGroup, $"The group `{group.Id}` has no nodes and no `x` and `y`, so it is not drawn.", group.Range.Start, true)));

        return [.. breaches.OrderBy(breach => breach.Line)];
    }

    private static DiagramProblem ToProblem(SupplyChainBreach breach) => new(
        breach.IsWarning ? DiagramProblemSeverity.Warning : DiagramProblemSeverity.Error,
        breach.Message,
        breach.RuleId,
        new DiagramProblemLineLocation((uint)Math.Max(breach.Line, 0) + 1));
}
