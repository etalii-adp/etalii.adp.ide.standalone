using System.Text.Json;

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

        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("operations", out var operations)
            || !operations.TryGetProperty(operationId, out var operation))
        {
            return DislTransaction.Refused($"There is no operation '{operationId}'.");
        }

        var pointer = DislJson.Pointer("/behavior/operations", operationId);
        var variables = Variables(DislContexts.Operation, diagram, env);
        variables["self"] = self;
        variables["selection"] = invocation?.Selection?.Cast<object?>().ToList() ?? (self is null ? [] : [self]);
        variables["position"] = Map(invocation?.Position);
        variables["p"] = Map(invocation?.Parameters);

        if (Unavailable(specification, operation, pointer, diagram, self is null ? null : self.IsA, variables, operationId) is { } reason) return DislTransaction.Refused(reason);

        return Actions(specification, operation, pointer, diagram, ids, variables);
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
        if (relation.Derived is not { ValueKind: JsonValueKind.Object } derived || !derived.TryGetProperty("edits", out var edits)
            || DislJson.String(edits, "connect") is not { } operationId)
        {
            return DislTransaction.Refused(relation.Derived is { ValueKind: JsonValueKind.Object } withReason && DislJson.String(withReason, "reason") is { } reason
                ? reason
                : $"A {relationType} cannot be drawn.");
        }
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("operations", out var operations)
            || !operations.TryGetProperty(operationId, out var operation))
        {
            return DislTransaction.Refused($"There is no operation '{operationId}'.");
        }

        var pointer = DislJson.Pointer("/behavior/operations", operationId);
        var variables = Variables(DislContexts.Operation, diagram, env);
        variables["self"] = new Cel.CelMap { ["source"] = source, ["target"] = target, ["type"] = relationType };
        variables["selection"] = new List<object?>();
        variables["position"] = Map(null);
        variables["p"] = Map(null);

        if (Unavailable(specification, operation, pointer, diagram, relation.Linearisation.Contains, variables, operationId) is { } refusal) return DislTransaction.Refused(refusal);

        return Actions(specification, operation, pointer, diagram, ids, variables);
    }

    /// <summary>The operation's <c>plugin</c> handed back whole, or its actions run.</summary>
    private static DislTransaction Actions(DislSpecification specification, JsonElement operation, string pointer, DislDiagram diagram, IIdSource ids, Dictionary<string, object?> variables)
    {
        if (operation.TryGetProperty("plugin", out var plugin))
        {
            var name = plugin.ValueKind == JsonValueKind.String ? plugin.GetString()! : DislJson.String(plugin, "name") ?? "";
            return new DislTransaction([], [new HostAction.Plugin(name, new Dictionary<string, object?>())], null);
        }

        var runner = new ActionRunner(specification, diagram, ids, DislContexts.Operation);
        if (operation.TryGetProperty("actions", out var actions)) runner.Run(actions, DislJson.Pointer(pointer, "actions"), variables);
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

        if (Tool(specification, toolId) is not var (tool, pointer)) return DislTransaction.Refused($"There is no tool '{toolId}'.");
        if (DislJson.String(tool, "creates") is not { } type || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false } created)
        {
            return DislTransaction.Refused($"Tool '{toolId}' creates no node.");
        }

        var variables = Variables(DislContexts.Create, diagram, env);
        variables["position"] = Map(position);
        variables["elementType"] = type;
        variables["tool"] = toolId;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in DislJson.Members(tool, "initial"))
        {
            if (!created.Attributes.TryGetValue(property.Name, out var attribute)) return DislTransaction.Refused($"{type} has no attribute '{property.Name}'.");
            var value = property.Value.ValueKind == JsonValueKind.Object
                ? DislEvaluation.Expression(specification, property.Value, DislJson.Pointer(DislJson.Pointer(pointer, "initial"), property.Name), DislContexts.Create, variables)
                : DislValues.FromJson(property.Value, attribute, specification.Metamodel);
            if (value is Cel.CelError error) return DislTransaction.Refused(error.Message);
            if (value is not null) attributes[property.Name] = value;
        }

        var element = diagram.AddNode(type, ids.Next(type), attributes);
        List<HostAction> after = DislJson.String(tool, "after") switch
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
        if (ConnectTool(specification, relationType) is not var (tool, pointer)) return DislTransaction.Refused($"No tool creates a {relationType} on empty canvas.");
        var key = newEnd == "target" ? "createTarget" : "createSource";
        if (!tool.TryGetProperty(key, out var end) || end.ValueKind == JsonValueKind.String && end.GetString() == "ask")
        {
            return DislTransaction.Refused($"A {relationType} cannot end on empty canvas.");
        }

        var type = end.ValueKind == JsonValueKind.String ? end.GetString() : DislJson.String(end, "type");
        if (type is null || specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false } created) return DislTransaction.Refused($"The {key} of {relationType} names no node type it can create.");

        var variables = Variables(DislContexts.Create, diagram, env);
        variables["position"] = Map(position);
        variables["elementType"] = type;
        variables["tool"] = DislJson.String(tool, "id");

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var initialAt = DislJson.Pointer(DislJson.Pointer(pointer, key), "initial");
        foreach (var property in DislJson.Members(end, "initial"))
        {
            if (!created.Attributes.TryGetValue(property.Name, out var attribute)) return DislTransaction.Refused($"{type} has no attribute '{property.Name}'.");
            var value = property.Value.ValueKind == JsonValueKind.Object
                ? DislEvaluation.Expression(specification, property.Value, DislJson.Pointer(initialAt, property.Name), DislContexts.Create, variables)
                : DislValues.FromJson(property.Value, attribute, specification.Metamodel);
            if (value is Cel.CelError error) return DislTransaction.Refused(error.Message);
            if (value is not null) attributes[property.Name] = value;
        }

        try
        {
            var node = diagram.AddNode(type, ids.Next(type), attributes);
            var (source, target) = newEnd == "target" ? (existing, node) : (node, existing);
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
    /// Unlike <see cref="Unavailable(DislSpecification, string, DislDiagram, DislElement?, DislEnv?)"/> it asks
    /// nothing about the operation's state, so a host can tell an entry that is not offered here from one
    /// that is refused now.
    /// </summary>
    public static bool AppliesTo(DislSpecification specification, string operationId, DislElement? self)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("operations", out var operations)
            || !operations.TryGetProperty(operationId, out var operation))
        {
            return false;
        }
        var targets = DislJson.Strings(operation, "for");
        return targets.Count == 0 || targets.Contains("diagram") || targets.Contains("selection") || self is not null && targets.Any(self.IsA);
    }

    /// <summary>Why operation <paramref name="operationId"/> cannot run on <paramref name="self"/> now, or null when it can.</summary>
    public static string? Unavailable(DislSpecification specification, string operationId, DislDiagram diagram, DislElement? self, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(diagram);
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("operations", out var operations)
            || !operations.TryGetProperty(operationId, out var operation))
        {
            return $"There is no operation '{operationId}'.";
        }
        var variables = Variables(DislContexts.Operation, diagram, env);
        variables["self"] = self;
        variables["selection"] = self is null ? new List<object?>() : [self];
        return Unavailable(specification, operation, DislJson.Pointer("/behavior/operations", operationId), diagram, self is null ? null : self.IsA, variables, operationId);
    }

    internal static Dictionary<string, object?> Variables(string context, DislDiagram diagram, DislEnv? env)
    {
        var variables = DislContexts.VariablesOf(context).ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal);
        variables["diagram"] = diagram;
        variables["env"] = (env ?? new DislEnv()).ToCel();
        return variables;
    }

    /// <param name="isA">Whether the operation's target is of a type, or null when it runs on the diagram.</param>
    private static string? Unavailable(DislSpecification specification, JsonElement operation, string pointer, DislDiagram diagram, Func<string, bool>? isA, Dictionary<string, object?> variables, string operationId)
    {
        var applies = DislJson.Strings(operation, "for") is var targets && (targets.Count == 0 || targets.Contains("diagram") || targets.Contains("selection")
            || isA is not null && targets.Any(isA));
        if (!applies) return NotApplicable(specification, diagram, operationId);

        if (specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("editGate", out var gate)
            && DislEvaluation.FirstReason(specification, gate, "/behavior/editGate", DislContexts.Element, Gate(diagram, variables)) is { } gated)
        {
            return gated;
        }
        if (operation.TryGetProperty("unavailable", out var unavailable)
            && DislEvaluation.FirstReason(specification, unavailable, DislJson.Pointer(pointer, "unavailable"), DislContexts.Operation, variables) is { } reason)
        {
            return reason;
        }
        return operation.TryGetProperty("enabled", out var enabled) && !DislEvaluation.Holds(specification, enabled, DislJson.Pointer(pointer, "enabled"), DislContexts.Operation, variables)
            ? NotApplicable(specification, diagram, operationId)
            : null;
    }

    private static string NotApplicable(DislSpecification specification, DislDiagram diagram, string operationId)
    {
        var variables = Variables(DislContexts.Element, diagram, null);
        variables["self"] = diagram;
        variables["operationId"] = operationId;
        return DislEvaluation.StandardMessage(specification, "std.notApplicable", variables, "That does not apply to this selection.");
    }

    private static Dictionary<string, object?> Gate(DislDiagram diagram, Dictionary<string, object?> variables) =>
        new(StringComparer.Ordinal) { ["self"] = diagram, ["diagram"] = diagram, ["env"] = variables["env"] };

    /// <summary>The tool <paramref name="toolId"/> as the palette declares it, inline in a group or from the toolbox's library, with its pointer.</summary>
    private static (JsonElement Tool, string Pointer)? Tool(DislSpecification specification, string toolId)
    {
        if (!specification.Root.TryGetProperty("toolbox", out var toolbox)) return null;
        return toolbox.TryGetProperty("groups", out var groups) ? InGroups(groups, "/toolbox/groups") : null;

        (JsonElement, string)? InGroups(JsonElement list, string at)
        {
            if (list.ValueKind != JsonValueKind.Array) return null;
            var index = 0;
            foreach (var group in list.EnumerateArray())
            {
                var groupAt = DislJson.Pointer(at, index++);
                var position = 0;
                var tools = group.TryGetProperty("tools", out var declared) && declared.ValueKind == JsonValueKind.Array ? declared.EnumerateArray().ToList() : [];
                foreach (var entry in tools)
                {
                    var entryAt = DislJson.Pointer(DislJson.Pointer(groupAt, "tools"), position++);
                    if (entry.ValueKind == JsonValueKind.String && entry.GetString() == toolId
                        && toolbox.TryGetProperty("tools", out var library) && library.TryGetProperty(toolId, out var shared))
                    {
                        return (shared, DislJson.Pointer("/toolbox/tools", toolId));
                    }
                    if (entry.ValueKind == JsonValueKind.Object && DislJson.String(entry, "id") == toolId) return (entry, entryAt);
                }
                if (group.TryGetProperty("groups", out var nested) && InGroups(nested, DislJson.Pointer(groupAt, "groups")) is { } found) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// The tool a connect gesture of <paramref name="relationType"/> runs: the one its edge notation's
    /// <c>connect.tool</c> names (§6.10), else the first library tool (<c>toolbox.tools</c>) that creates it.
    /// </summary>
    private static (JsonElement Tool, string Pointer)? ConnectTool(DislSpecification specification, string relationType)
    {
        if (!specification.Root.TryGetProperty("toolbox", out var toolbox) || !toolbox.TryGetProperty("tools", out var library) || library.ValueKind != JsonValueKind.Object) return null;
        var named = specification.Root.TryGetProperty("notation", out var notation) && notation.TryGetProperty("edges", out var edges)
            && edges.TryGetProperty(relationType, out var edge) && edge.TryGetProperty("connect", out var connect)
                ? DislJson.String(connect, "tool")
                : null;
        foreach (var tool in library.EnumerateObject())
        {
            if (named is not null ? tool.Name == named : DislJson.String(tool.Value, "creates") == relationType) return (tool.Value, DislJson.Pointer("/toolbox/tools", tool.Name));
        }
        return null;
    }

    private static Cel.CelMap Map(IReadOnlyDictionary<string, object?>? values)
    {
        var map = new Cel.CelMap();
        foreach ((string key, object? value) in values ?? new Dictionary<string, object?>()) map[key] = value;
        return map;
    }
}
