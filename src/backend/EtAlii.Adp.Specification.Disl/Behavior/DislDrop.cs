namespace EtAlii.Adp.Specification.Disl;

/// <summary>What a toolbox tool dropped on a drop target comes to (DISL §7.2), before anything runs.</summary>
public abstract record DislDropOutcome
{
    /// <summary>
    /// A target of the tool's <c>drop.targets</c> matched: its context tool, of <paramref name="Kind"/> running
    /// <paramref name="Operation"/> for an <c>operation</c>, runs on <paramref name="On"/> (null for the diagram,
    /// as <c>"canvas"</c> gives it).
    /// </summary>
    public sealed record Target(string Kind, string? Operation, DislElement? On) : DislDropOutcome
    {
        /// <summary>
        /// The matching target's <c>args</c>, evaluated where the drop lands (§7.2, §7.3): <c>self</c> the drop
        /// target (the diagram on empty canvas) and <c>position</c> the drop point; empty for a target that
        /// states none, and for a tool without <c>drop</c>.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The drop is refused with <paramref name="Reason"/>: a matching target's operation is unavailable there,
    /// or no target matched and <c>elsewhere</c> is <c>"refuse"</c>.
    /// </summary>
    public sealed record Refused(string Reason) : DislDropOutcome;

    /// <summary>No target matched and <c>elsewhere</c> is <paramref name="What"/>: <c>"create"</c>, <c>"ignore"</c> or <c>"menu"</c>.</summary>
    public sealed record Elsewhere(string What) : DislDropOutcome;

    /// <summary>The drop is placed by geometry, by the parent placement its <c>place</c> names, which consults no target.</summary>
    public sealed record Placed : DislDropOutcome;
}

/// <summary>
/// Resolves a toolbox tool dropped on a drop target (DISL §7.2): its <c>drop.targets</c> tried in order against the
/// drop target's type, <c>on</c> matching subtypes and <c>"canvas"</c> matching no element; the first match is the
/// outcome, refused with its reason when its operation is unavailable there; with no match, <c>elsewhere</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is run</b>: a host runs the outcome its own way, as one transaction, typically after asking what the
/// operation asks. That is what lets a host resolve a drop by the entry before it asks the user anything.
/// </para>
/// <para>
/// <b>A refusal</b> is evaluated in the <c>create</c> context with <c>dropTarget</c> and <c>tool</c> bound (§12.3);
/// a tool that states none is refused with <see cref="DefaultRefusal"/>.
/// </para>
/// <para>
/// <b>A matching target's <c>args</c></b> are Expressions, as a context tool's are (§7.3), evaluated in the element
/// context with <c>self</c> the drop target, the diagram on empty canvas, and <c>position</c> the drop point; what
/// they give is the outcome's <see cref="DislDropOutcome.Target.Arguments"/>, for the host to hand the operation.
/// </para>
/// <para>
/// <b>A tool without <c>drop</c></b> is resolved as §7.2 says of DISL 0.1: an <c>operation</c> tool runs on the drop
/// target when the operation's <c>for</c> admits it, else on the diagram; any other tool creates at the point.
/// </para>
/// </remarks>
public static class DislDrop
{
    /// <summary>The runtime's own sentence for a drop refused by a tool that words none.</summary>
    public const string DefaultRefusal = "That cannot be dropped here.";

    /// <summary>
    /// What tool <paramref name="toolId"/> dropped on <paramref name="dropTarget"/> (null on empty canvas) of
    /// <paramref name="diagram"/> at <paramref name="position"/> (null for none) comes to; null when the
    /// specification has no such tool.
    /// </summary>
    public static DislDropOutcome? Resolve(
        DislSpecification specification,
        string toolId,
        DislDiagram diagram,
        DislElement? dropTarget,
        DislEnv? env = null,
        IReadOnlyDictionary<string, object?>? position = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(toolId);
        ArgumentNullException.ThrowIfNull(diagram);

        if (specification.Toolbox.Find(toolId) is not { } tool) return null;
        if (tool.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (tool.Drop is not { } drop) return WithoutDrop(specification, tool, dropTarget);
        if (drop.Place is not null) return new DislDropOutcome.Placed();

        foreach (var target in drop.Targets)
        {
            var matches = dropTarget is null ? target.On.Contains("canvas") : target.On.Any(dropTarget.IsA);
            if (!matches) continue;

            return target is { Kind: "operation", Operation: { } operation } && OperationInterpreter.Refusal(specification, operation, diagram, dropTarget, env) is { } reason
                ? new DislDropOutcome.Refused(reason)
                : new DislDropOutcome.Target(target.Kind, target.Operation, dropTarget) { Arguments = Arguments(target, diagram, dropTarget, env, position) };
        }

        if (drop.Elsewhere != "refuse") return new DislDropOutcome.Elsewhere(drop.Elsewhere);

        var variables = OperationInterpreter.Variables(DislContext.Create, diagram, env);
        variables["dropTarget"] = dropTarget;
        variables["tool"] = toolId;
        variables["elementType"] = tool.Creates;
        return new DislDropOutcome.Refused(drop.Refusal is null ? DefaultRefusal : DislReason.First(drop.Refusal, specification, variables) ?? DefaultRefusal);
    }

    /// <summary>A matching target's <c>args</c>, evaluated in the element context where the drop lands.</summary>
    private static Dictionary<string, object?> Arguments(
        DislDropTarget target,
        DislDiagram diagram,
        DislElement? dropTarget,
        DislEnv? env,
        IReadOnlyDictionary<string, object?>? position)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (target.Arguments.Count == 0) return arguments;

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["self"] = (object?)dropTarget ?? diagram,
            ["diagram"] = diagram,
            ["env"] = (env ?? new DislEnv()).ToCel(),
            ["position"] = OperationInterpreter.Map(position),
        };
        foreach ((string name, DislExpressionField value) in target.Arguments) arguments[name] = value.Evaluate(variables);

        return arguments;
    }

    /// <summary>A tool without <c>drop</c>, as DISL 0.1 drops it.</summary>
    private static DislDropOutcome WithoutDrop(DislSpecification specification, DislTool tool, DislElement? dropTarget)
    {
        if (tool.Kind != "operation" || tool.Operation is not { } operation) return new DislDropOutcome.Elsewhere("create");
        return new DislDropOutcome.Target("operation", operation, OperationInterpreter.AppliesTo(specification, operation, dropTarget) ? dropTarget : null);
    }
}
