
namespace EtAlii.Adp.Specification.Disl;

/// <summary>What an operation is invoked with besides its target (DISL §9.3, §12.3).</summary>
/// <param name="Position">The domain point it was invoked at, such as a context menu's on empty canvas: a map with <c>x</c> and <c>y</c>.</param>
/// <param name="Selection">The selected elements, for an operation <c>for: "selection"</c>.</param>
/// <param name="Parameters">The answered parameters, bound as <c>p</c>.</param>
public sealed record DislInvocation(IReadOnlyDictionary<string, object?>? Position = null, IReadOnlyList<DislElement>? Selection = null, IReadOnlyDictionary<string, object?>? Parameters = null);

/// <summary>
/// Runs a definition's operations (DISL §9.3) and a toolbox tool's drop (§7.1) as one transaction
/// each, into <see cref="DislChange"/>s a host writes and <see cref="HostAction"/>s it carries out.
/// </summary>
/// <remarks>
/// <para>
/// <b>The diagram given is the transaction's working state</b>, changed as the actions run
/// (<see cref="ActionRunner"/>); a host gives each transaction a diagram of its own.
/// </para>
/// <para>
/// <b>An operation that is not available is refused with its reason</b>: the edit gate's
/// (§9.1), the first of its <c>unavailable</c> reasons that applies, or, when only <c>enabled</c> is
/// false, <c>std.notApplicable</c>. A <c>plugin</c> operation is handed back whole.
/// </para>
/// </remarks>
public static class OperationInterpreter
{
    /// <summary>Runs operation <paramref name="operationId"/> on <paramref name="self"/> (null for one <c>for: "diagram"</c>).</summary>
    public static DislTransaction Run(
        DislSpecification specification,
        string operationId,
        DislDiagram diagram,
        DislElement? self,
        IIdSource ids,
        DislInvocation? invocation = null,
        DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(ids);

        if (specification.Operations.Find(operationId) is not { } operation) return DislTransaction.Refused($"There is no operation '{operationId}'.");

        var variables = Variables(DislContext.Operation, diagram, env);
        variables["self"] = self;
        variables["selection"] = invocation?.Selection?.Cast<object?>().ToList() ?? (self is null ? [] : [self]);
        variables["position"] = Map(invocation?.Position);
        variables["p"] = Map(invocation?.Parameters);

        if (Unavailable(specification, operation, diagram, self is null ? null : self.IsA, variables) is { } reason) return DislTransaction.Refused(reason);

        return Actions(specification, operation, diagram, ids, variables);
    }

    /// <summary>
    /// Runs the operation a derived relation type's <c>edits.connect</c> names for a new line of
    /// <paramref name="relationType"/> from <paramref name="source"/> to <paramref name="target"/> (§4.11.4), as one
    /// transaction. The relation does not exist yet, so <c>self</c> is its two ends and its type - a map
    /// with <c>source</c>, <c>target</c> and <c>type</c>, as the context menu reads it - and the operation
    /// applies when its <c>for</c> names the relation type or one it inherits from.
    /// </summary>
    public static DislTransaction Connect(
        DislSpecification specification,
        string relationType,
        DislDiagram diagram,
        DislElement source,
        DislElement target,
        IIdSource ids,
        DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(relationType);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ids);

        if (specification.Metamodel.TypeOf(relationType) is not { IsRelation: true } relation) return DislTransaction.Refused($"There is no relation type '{relationType}'.");
        if (relation.ConnectEdit is not { } operationId) return DislTransaction.Refused(relation.DerivedReason ?? $"A {relationType} cannot be drawn.");
        if (specification.Operations.Find(operationId) is not { } operation) return DislTransaction.Refused($"There is no operation '{operationId}'.");

        var variables = Variables(DislContext.Operation, diagram, env);
        variables["self"] = new Cel.CelMap { ["source"] = source, ["target"] = target, ["type"] = relationType };
        variables["selection"] = new List<object?>();
        variables["position"] = Map(null);
        variables["p"] = Map(null);

        if (Unavailable(specification, operation, diagram, relation.Linearisation.Contains, variables) is { } refusal) return DislTransaction.Refused(refusal);

        return Actions(specification, operation, diagram, ids, variables);
    }

    /// <summary>
    /// The operation's <c>plugin</c> handed back whole, its <c>args</c> as written (a PluginCall's
    /// arguments are values validated against the plugin's declaration, not expressions), or its actions run.
    /// </summary>
    private static DislTransaction Actions(DislSpecification specification, DislOperation operation, DislDiagram diagram, IIdSource ids, Dictionary<string, object?> variables)
    {
        if (operation.Plugin is { } plugin)
        {
            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach ((string name, DislActionValue.Literal value) in plugin.Arguments) arguments[name] = value.Evaluate(variables);
            return new DislTransaction([], [new HostAction.Plugin(plugin.Name, arguments)], null);
        }

        var runner = new ActionRunner(specification, diagram, ids);
        runner.Run(operation.Actions, variables);
        return runner.Transaction;
    }

    /// <summary>
    /// Drops toolbox tool <paramref name="toolId"/> at <paramref name="position"/> (§7.1): a <c>create</c>
    /// of its type with its <c>initial</c> values, and its <c>after</c> (<c>select</c> or <c>editLabel</c>) handed back.
    /// </summary>
    public static DislTransaction Drop(
        DislSpecification specification,
        string toolId,
        DislDiagram diagram,
        IReadOnlyDictionary<string, object?> position,
        IIdSource ids,
        DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(ids);

        if (specification.Toolbox.Find(toolId) is not { } tool) return DislTransaction.Refused($"There is no tool '{toolId}'.");
        if (tool.Creates is not { } type || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false } created)
        {
            return DislTransaction.Refused($"Tool '{toolId}' creates no node.");
        }

        var variables = Variables(DislContext.Create, diagram, env);
        variables["position"] = Map(position);
        variables["elementType"] = type;
        variables["tool"] = toolId;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (Initial(tool.Initial, type, created, variables, attributes) is { } refusal) return DislTransaction.Refused(refusal);

        var element = diagram.AddNode(type, ids.Next(type), attributes);
        List<HostAction> after = tool.After switch
        {
            "editLabel" => [new HostAction.EditLabel(element.Id, null)],
            "select" or null => [new HostAction.Select([element.Id])],
            _ => [],
        };
        return new DislTransaction([new DislChange.Create(type, element.Id, attributes, null, Map(position))], after, null);
    }

    /// <summary>
    /// Runs a connect gesture of <paramref name="relationType"/> released on empty canvas (§6.10, §7.2): the
    /// missing end - the <paramref name="newEnd"/>, <c>source</c> or <c>target</c> - created at
    /// <paramref name="position"/> from the <c>createSource</c> or <c>createTarget</c> of the tool the edge's
    /// <c>connect.tool</c> names, else of a toolbox tool that creates the relation type, and the relation
    /// between it and <paramref name="existing"/>, as one transaction: a <see cref="DislChange.Create"/>, then a
    /// <see cref="DislChange.Connect"/>.
    /// </summary>
    /// <remarks>
    /// A CreateEnd's <c>initial</c> is evaluated in the <c>create</c> context (§12.3) with <c>position</c> the
    /// release point in domain values; a bare type creates the node with no attributes. A tool that states no
    /// such end, or <c>"ask"</c>, is refused: the runtime has no menu of targets to offer.
    /// </remarks>
    public static DislTransaction ConnectToNew(
        DislSpecification specification,
        string relationType,
        DislDiagram diagram,
        DislElement existing,
        string newEnd,
        IReadOnlyDictionary<string, object?> position,
        IIdSource ids,
        DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(relationType);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(ids);
        if (newEnd is not ("source" or "target")) throw new ArgumentException("The new end is the source or the target.", nameof(newEnd));

        if (specification.Metamodel.TypeOf(relationType) is not { IsRelation: true, Abstract: false }) return DislTransaction.Refused($"There is no relation type '{relationType}'.");
        if (ConnectTool(specification, relationType) is not { } tool) return DislTransaction.Refused($"No tool creates a {relationType} on empty canvas.");
        if (tool.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        var key = newEnd == "target" ? "createTarget" : "createSource";
        if ((newEnd == "target" ? tool.CreateTarget : tool.CreateSource) is not { Ask: false } end)
        {
            return DislTransaction.Refused($"A {relationType} cannot end on empty canvas.");
        }

        var type = end.Type;
        if (type is null || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false } created) return DislTransaction.Refused($"The {key} of {relationType} names no node type it can create.");

        var variables = Variables(DislContext.Create, diagram, env);
        variables["position"] = Map(position);
        variables["elementType"] = type;
        variables["tool"] = tool.Id;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (Initial(end.Initial, type, created, variables, attributes) is { } refusal) return DislTransaction.Refused(refusal);

        try
        {
            var node = diagram.AddNode(type, ids.Next(type), attributes);
            (DislElement source, DislElement target) = newEnd == "target" ? (existing, node) : (node, existing);
            var relation = diagram.AddRelation(relationType, ids.Next(relationType), source, target);
            return new DislTransaction(
                [
                    new DislChange.Create(type, node.Id, attributes, null, Map(position)),
                    new DislChange.Connect(relationType, relation.Id, source.Id, target.Id, new Dictionary<string, object?>(StringComparer.Ordinal)),
                ],
                [],
                null);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return DislTransaction.Refused(e.Message);
        }
    }

    /// <summary>
    /// Whether operation <paramref name="operationId"/> is for <paramref name="self"/> (null for the diagram):
    /// its <c>for</c> names the element's type or one it inherits from, or the diagram, the selection, or nothing.
    /// Unlike the availability check a run makes, it asks
    /// nothing about the operation's state, so a host can tell an entry that is not offered here from one
    /// that is refused now.
    /// </summary>
    public static bool AppliesTo(DislSpecification specification, string operationId, DislElement? self)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return specification.Operations.Find(operationId) is { } operation && operation.AppliesTo(self is null ? null : self.IsA);
    }

    /// <summary>
    /// Why operation <paramref name="operationId"/> would be refused on <paramref name="self"/> (null for the
    /// diagram) before any of its actions ran: the reason a <see cref="Run"/> gives for an operation that is not
    /// available (§9.3), or null when it is. No parameter is answered, so a reason that reads <c>p</c> sees none.
    /// </summary>
    public static string? Refusal(DislSpecification specification, string operationId, DislDiagram diagram, DislElement? self, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(diagram);
        if (specification.Operations.Find(operationId) is not { } operation) return $"There is no operation '{operationId}'.";

        var variables = Variables(DislContext.Operation, diagram, env);
        variables["self"] = self;
        variables["selection"] = self is null ? new List<object?>() : [self];
        variables["position"] = Map(null);
        variables["p"] = Map(null);
        return Unavailable(specification, operation, diagram, self is null ? null : self.IsA, variables);
    }

    internal static Dictionary<string, object?> Variables(DislContext context, DislDiagram diagram, DislEnv? env)
    {
        var variables = DislContexts.VariablesOf(context).ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal);
        variables["diagram"] = diagram;
        variables["env"] = (env ?? new DislEnv()).ToCel();
        return variables;
    }

    /// <param name="isA">Whether the operation's target is of a type, or null when it runs on the diagram.</param>
    internal static string? Unavailable(DislSpecification specification, DislOperation operation, DislDiagram diagram, Func<string, bool>? isA, Dictionary<string, object?> variables)
    {
        if (!operation.AppliesTo(isA)) return NotApplicable(specification, diagram, operation.Id);

        if (DislReason.First(specification.Operations.EditGate, specification, Gate(diagram, variables)) is { } gated) return gated;
        if (DislBudgets.Changes(operation) is { } changes && DislBudgets.WithheldReason(diagram, changes == "view") is { } withheld) return withheld;

        // An operation that is not an object threw here when it was read from the JSON at run time, and still does.
        if (operation.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (DislReason.First(operation.Unavailable, specification, variables) is { } reason) return reason;
        return operation.Enabled is { } enabled && !enabled.Holds(variables)
            ? NotApplicable(specification, diagram, operation.Id)
            : null;
    }

    private static string NotApplicable(DislSpecification specification, DislDiagram diagram, string operationId)
    {
        var variables = Variables(DislContext.Element, diagram, null);
        variables["self"] = diagram;
        variables["operationId"] = operationId;
        return DislEvaluation.StandardMessage(specification, "std.notApplicable", variables, "That does not apply to this selection.");
    }

    private static Dictionary<string, object?> Gate(DislDiagram diagram, Dictionary<string, object?> variables) =>
        new(StringComparer.Ordinal) { ["self"] = diagram, ["diagram"] = diagram, ["env"] = variables["env"] };

    /// <summary>
    /// The tool a connect gesture of <paramref name="relationType"/> runs: the one its edge notation's
    /// <c>connect.tool</c> names (§6.10), else the first library tool (<c>toolbox.tools</c>) that creates it.
    /// </summary>
    private static DislTool? ConnectTool(DislSpecification specification, string relationType)
    {
        if (specification.Toolbox.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (specification.Toolbox.Library is not { } library) return null;
        var named = specification.Notation.ConnectToolOf(relationType);
        foreach ((string name, DislTool tool) in library)
        {
            if (named is not null ? name == named : tool.Creates == relationType) return tool;
        }
        return null;
    }

    /// <summary>
    /// The <paramref name="initial"/> values of a node of <paramref name="type"/> into <paramref name="attributes"/>: an
    /// Expression evaluated in the create context, any other value as the loader read it for its attribute; the refusal, or null.
    /// </summary>
    private static string? Initial(
        IReadOnlyList<DislToolInitial> initial,
        string type,
        DislType created,
        Dictionary<string, object?> variables,
        Dictionary<string, object?> attributes)
    {
        foreach (var property in initial)
        {
            if (!created.Attributes.ContainsKey(property.Name)) return $"{type} has no attribute '{property.Name}'.";
            var value = property.Expression is { } expression
                ? expression.Evaluate(variables)
                : DislValues.Copy(property.Literal);
            if (value is Cel.CelError error) return error.Message;
            if (value is not null) attributes[property.Name] = value;
        }
        return null;
    }

    internal static Cel.CelMap Map(IReadOnlyDictionary<string, object?>? values)
    {
        var map = new Cel.CelMap();
        foreach ((string key, object? value) in values ?? new Dictionary<string, object?>()) map[key] = value;
        return map;
    }
}
