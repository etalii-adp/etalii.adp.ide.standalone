using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>What a context menu is opened on (DISL §7.3): an element, the empty canvas, or a pending connection.</summary>
public abstract record DislMenuTarget(DislDiagram Diagram)
{
    /// <summary>A node or relation: the sets whose <c>for</c> names its type or a supertype.</summary>
    public static DislMenuTarget Element(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new OnElement(element);
    }

    /// <summary>The empty canvas: the sets <c>for</c> <c>diagram</c>, <c>self</c> bound to the diagram.</summary>
    public static DislMenuTarget Canvas(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return new OnCanvas(diagram);
    }

    /// <summary>
    /// A finished connect gesture from <paramref name="source"/> to <paramref name="target"/>, either
    /// of which may name nothing: the sets <c>for</c> <c>connection</c>, else the connect edit of
    /// <paramref name="relationType"/> when it is a derived relation (§4.11).
    /// </summary>
    public static DislMenuTarget Connection(DislDiagram diagram, DislElement? source, DislElement? target, string? relationType = null)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return new OnConnection(diagram, source, target, relationType);
    }

    internal sealed record OnElement(DislElement Self) : DislMenuTarget(Self.Diagram);

    internal sealed record OnCanvas(DislDiagram Self) : DislMenuTarget(Self);

    internal sealed record OnConnection(DislDiagram On, DislElement? Source, DislElement? Target, string? RelationType) : DislMenuTarget(On);
}

/// <summary>A keyboard shortcut as a specification writes it (<c>Alt+Up</c>), and its parts.</summary>
/// <param name="Text">As written.</param>
/// <param name="Key">The key: the text after the last modifier.</param>
public sealed record DerivedShortcut(string Text, string Key, bool Ctrl, bool Shift, bool Alt, bool Meta)
{
    /// <summary>Reads <c>Ctrl</c>, <c>Shift</c>, <c>Alt</c> and <c>Meta</c> (and <c>Control</c>, <c>Option</c>, <c>Cmd</c>) before the key, joined by <c>+</c>.</summary>
    public static DerivedShortcut Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('+');
        bool ctrl = false, shift = false, alt = false, meta = false;
        var used = 0;
        for (; used < parts.Length - 1; used++)
        {
            switch (parts[used].Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt" or "option": alt = true; break;
                case "meta" or "cmd" or "command": meta = true; break;
                default: return new DerivedShortcut(text, string.Join('+', parts[used..]), ctrl, shift, alt, meta);
            }
        }
        return new DerivedShortcut(text, parts[^1], ctrl, shift, alt, meta);
    }
}

/// <summary>One entry of a context menu as a host shows it, its id mapped by a <see cref="WireIdMap"/>.</summary>
/// <param name="Id">The action id.</param>
/// <param name="Kind">The entry's kind (<c>operation</c>, <c>delete</c>, <c>editLabel</c>, …).</param>
/// <param name="Operation">The operation it runs, or null.</param>
/// <param name="UnavailableReason">Why it cannot run now; empty when <paramref name="Available"/>.</param>
/// <param name="Arguments">Its <c>args</c>, evaluated, and the name a <c>forEach</c> binds; empty for most entries.</param>
public sealed record DerivedMenuEntry(
    string Id,
    string Kind,
    string? Operation,
    string Label,
    string Icon,
    DerivedShortcut? Shortcut,
    bool Available,
    string UnavailableReason,
    IReadOnlyDictionary<string, object?> Arguments);

/// <summary>One group of a context menu: consecutive entries with one <c>x-menu.group</c> name (null when they state none).</summary>
public sealed record DerivedMenuGroup(string? Name, IReadOnlyList<DerivedMenuEntry> Entries);

/// <summary>
/// The context menu of a target (DISL §7.3): the matching sets of <c>toolbox.contextMenus</c> whose
/// <c>when</c> holds, in declaration order; each entry once, or once per item of its <c>forEach</c>,
/// unless its <c>visible</c> fails; and each entry's availability from its own <c>unavailable</c>
/// reasons, then its operation's, then <c>enabled</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Groups</b> follow the proposed key <c>x-menu.group</c> (decision D2): consecutive entries of
/// one set with an equal name, or with none, form one group, decided by the entries as declared; a
/// group whose entries are all hidden is dropped, so a menu with nothing visible has no groups.
/// </para>
/// <para>
/// <b><c>moveUp</c> and <c>moveDown</c></b> are unavailable at the first and the last place among
/// their siblings, with <c>behavior.messages</c>' <c>std.atStart</c> and <c>std.atEnd</c> (§7.3).
/// </para>
/// </remarks>
public static class ContextMenuDerivation
{
    public static IReadOnlyList<DerivedMenuGroup> Derive(DislSpecification specification, DislMenuTarget target, DislEnv env, WireIdMap ids)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(ids);

        var groups = new List<DerivedMenuGroup>();
        var cel = env.ToCel();
        var (context, variables, types) = target switch
        {
            DislMenuTarget.OnElement element => (DislContexts.Element, Variables(element.Self, target.Diagram, cel), element.Self.Type.Linearisation),
            DislMenuTarget.OnConnection connection => (DislContexts.Connection, Connection(connection, cel), (IReadOnlyList<string>)["connection"]),
            _ => (DislContexts.Element, Variables(target.Diagram, target.Diagram, cel), (IReadOnlyList<string>)["diagram"]),
        };

        var matched = false;
        if (specification.Root.TryGetProperty("toolbox", out var toolbox) && toolbox.TryGetProperty("contextMenus", out var sets) && sets.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var set in sets.EnumerateArray())
            {
                var pointer = DislJson.Pointer("/toolbox/contextMenus", index++);
                if (!Applies(set, target)) continue;
                matched = true;
                if (set.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(pointer, "when"), context, variables)) continue;
                Set(specification, set, pointer, context, variables, types, target, ids, groups);
            }
        }

        if (!matched && target is DislMenuTarget.OnConnection { RelationType: { } relation } pending)
        {
            ConnectEdit(specification, pending, relation, cel, ids, groups);
        }
        return groups;
    }

    private static bool Applies(JsonElement set, DislMenuTarget target)
    {
        var declared = set.TryGetProperty("for", out _);
        var names = DislJson.Strings(set, "for");
        return target switch
        {
            DislMenuTarget.OnElement element => !declared || names.Any(name => name is not ("diagram" or "connection") && element.Self.IsA(name)),
            DislMenuTarget.OnCanvas => names.Contains("diagram"),
            DislMenuTarget.OnConnection => names.Contains("connection"),
            _ => false,
        };
    }

    private static void Set(
        DislSpecification specification,
        JsonElement set,
        string pointer,
        string context,
        Dictionary<string, object?> variables,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids,
        List<DerivedMenuGroup> groups)
    {
        if (!set.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array) return;

        List<DerivedMenuEntry>? current = null;
        string? currentName = null;
        var index = 0;
        foreach (var tool in tools.EnumerateArray())
        {
            var at = DislJson.Pointer(DislJson.Pointer(pointer, "tools"), index);
            var name = DislJson.String(tool, "x-menu.group");
            if (current is null || name != currentName)
            {
                if (current is { Count: > 0 }) groups.Add(new DerivedMenuGroup(currentName, current));
                current = [];
                currentName = name;
            }
            index++;
            current.AddRange(Entries(specification, tool, at, context, variables, types, target, ids));
        }
        if (current is { Count: > 0 }) groups.Add(new DerivedMenuGroup(currentName, current));
    }

    private static IEnumerable<DerivedMenuEntry> Entries(
        DislSpecification specification,
        JsonElement tool,
        string pointer,
        string context,
        Dictionary<string, object?> variables,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids)
    {
        if (!tool.TryGetProperty("forEach", out var forEach))
        {
            if (Entry(specification, tool, pointer, context, variables, new Dictionary<string, object?>(), types, target, ids) is { } entry) yield return entry;
            yield break;
        }

        // An entry per item, in list order, with item (or its as name) and index bound (§7.3); an empty or failing list gives none.
        if (DislEvaluation.Expression(specification, forEach, DislJson.Pointer(pointer, "forEach"), context, variables) is not IReadOnlyList<object?> items) yield break;
        var named = DislJson.String(tool, "as");
        for (var index = 0; index < items.Count; index++)
        {
            var bindings = new Dictionary<string, object?>(StringComparer.Ordinal) { ["item"] = items[index], ["index"] = (long)index };
            if (named is not null) bindings[named] = items[index];
            if (Entry(specification, tool, pointer, context, variables, bindings, types, target, ids) is { } entry) yield return entry;
        }
    }

    private static DerivedMenuEntry? Entry(
        DislSpecification specification,
        JsonElement tool,
        string pointer,
        string context,
        Dictionary<string, object?> outer,
        Dictionary<string, object?> bindings,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids)
    {
        var variables = bindings.Count == 0 ? outer : new Dictionary<string, object?>(outer).Also(bindings);
        if (tool.TryGetProperty("visible", out var visible) && !DislEvaluation.Holds(specification, visible, DislJson.Pointer(pointer, "visible"), context, variables)) return null;

        var kind = DislJson.String(tool, "kind") ?? "operation";
        var operationName = DislJson.String(tool, "operation");
        var operation = Operation(specification, operationName);

        var arguments = new Dictionary<string, object?>(bindings, StringComparer.Ordinal);
        foreach (var argument in DislJson.Members(tool, "args"))
        {
            arguments[argument.Name] = DislEvaluation.Expression(specification, argument.Value, DislJson.Pointer(DislJson.Pointer(pointer, "args"), argument.Name), context, variables);
        }

        var fallback = operation is { } declared && declared.TryGetProperty("label", out var operationLabel)
            ? DislEvaluation.Message(specification, operationLabel, DislJson.Pointer(OperationPointer(operationName!), "label"), DislContexts.Operation, Operational(target, variables, arguments), kind)
            : DislJson.String(tool, "via") is { } via && specification.Metamodel.TypeOf(via) is { } viaType ? DislJson.String(viaType.Json, "label") ?? via : kind;
        var label = tool.TryGetProperty("label", out var message)
            ? DislEvaluation.Message(specification, message, DislJson.Pointer(pointer, "label"), context, variables, fallback)
            : fallback;
        var icon = DislJson.String(tool, "icon") ?? (operation is { } withIcon ? DislJson.String(withIcon, "icon") : null) ?? "";
        var shortcut = DislJson.String(tool, "shortcut") ?? (operation is { } withShortcut ? DislJson.String(withShortcut, "shortcut") : null);

        var reason = Unavailable(specification, tool, pointer, context, variables, kind, operation, operationName, target, arguments);
        var available = reason is null && (!tool.TryGetProperty("enabled", out var enabled) || DislEvaluation.Holds(specification, enabled, DislJson.Pointer(pointer, "enabled"), context, variables));

        return new DerivedMenuEntry(
            ids.ActionId(types, kind, operationName, arguments),
            kind,
            operationName,
            label,
            icon,
            shortcut is null ? null : DerivedShortcut.Parse(shortcut),
            available,
            reason ?? "",
            arguments);
    }

    private static string? Unavailable(
        DislSpecification specification,
        JsonElement tool,
        string pointer,
        string context,
        IReadOnlyDictionary<string, object?> variables,
        string kind,
        JsonElement? operation,
        string? operationName,
        DislMenuTarget target,
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (tool.TryGetProperty("unavailable", out var own)
            && DislEvaluation.FirstReason(specification, own, DislJson.Pointer(pointer, "unavailable"), context, variables) is { } reason)
        {
            return reason;
        }
        if (operation is { } declared && declared.TryGetProperty("unavailable", out var inherited)
            && DislEvaluation.FirstReason(specification, inherited, DislJson.Pointer(OperationPointer(operationName!), "unavailable"), DislContexts.Operation, Operational(target, variables, arguments)) is { } inheritedReason)
        {
            return inheritedReason;
        }
        if (kind is "moveUp" or "moveDown" && target is DislMenuTarget.OnElement { Self: { } self })
        {
            var siblings = self.Parent?.Children ?? [.. self.Diagram.Nodes.Where(node => node.Parent is null)];
            var place = siblings.ToList().FindIndex(sibling => ReferenceEquals(sibling, self));
            if (kind == "moveUp" && place <= 0) return DislEvaluation.StandardMessage(specification, "std.atStart", variables, "It is already the first.");
            if (kind == "moveDown" && place >= siblings.Count - 1) return DislEvaluation.StandardMessage(specification, "std.atEnd", variables, "It is already the last.");
        }
        return null;
    }

    /// <summary>The menu of a connection on a derived relation type with a <c>connect</c> edit (§4.11): its operation, as one entry.</summary>
    private static void ConnectEdit(DislSpecification specification, DislMenuTarget.OnConnection pending, string relation, CelMap env, WireIdMap ids, List<DerivedMenuGroup> groups)
    {
        if (specification.Metamodel.TypeOf(relation)?.Derived is not { ValueKind: JsonValueKind.Object } derived) return;
        if (!derived.TryGetProperty("edits", out var edits) || DislJson.String(edits, "connect") is not { } operationName) return;
        if (Operation(specification, operationName) is not { } operation) return;

        // The relation does not exist yet: self is its two ends, as the operation's reasons read them.
        var self = new CelMap { ["source"] = pending.Source, ["target"] = pending.Target, ["type"] = relation };
        var variables = new Dictionary<string, object?>
        {
            ["self"] = self, ["selection"] = new List<object?>(), ["p"] = new CelMap(), ["diagram"] = pending.Diagram, ["env"] = env, ["position"] = null,
        };
        var at = OperationPointer(operationName);
        var label = operation.TryGetProperty("label", out var message)
            ? DislEvaluation.Message(specification, message, DislJson.Pointer(at, "label"), DislContexts.Operation, variables, operationName)
            : operationName;
        var reason = operation.TryGetProperty("unavailable", out var reasons)
            ? DislEvaluation.FirstReason(specification, reasons, DislJson.Pointer(at, "unavailable"), DislContexts.Operation, variables)
            : null;
        var entry = new DerivedMenuEntry(
            ids.ActionId([relation, "connection"], "connect", operationName, new Dictionary<string, object?>()),
            "operation",
            operationName,
            label,
            DislJson.String(operation, "icon") ?? "",
            DislJson.String(operation, "shortcut") is { } shortcut ? DerivedShortcut.Parse(shortcut) : null,
            reason is null,
            reason ?? "",
            new Dictionary<string, object?>());
        groups.Add(new DerivedMenuGroup(null, [entry]));
    }

    private static JsonElement? Operation(DislSpecification specification, string? name) =>
        name is not null && specification.Root.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("operations", out var operations)
        && operations.TryGetProperty(name, out var operation) && operation.ValueKind == JsonValueKind.Object
            ? operation
            : null;

    private static string OperationPointer(string name) => DislJson.Pointer("/behavior/operations", name);

    /// <summary>The operation context's variables (§12.3) for an entry: its target as <c>self</c> and its selection, its arguments as <c>p</c>.</summary>
    private static Dictionary<string, object?> Operational(DislMenuTarget target, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, object?> arguments)
    {
        object? self = target switch
        {
            DislMenuTarget.OnElement element => element.Self,
            DislMenuTarget.OnCanvas => target.Diagram,
            _ => null,
        };
        var p = new CelMap();
        foreach (var (name, value) in arguments) p[name] = value;
        return new Dictionary<string, object?>(variables)
        {
            ["self"] = self,
            ["selection"] = self is DislElement ? new List<object?> { self } : new List<object?>(),
            ["p"] = p,
            ["diagram"] = target.Diagram,
            ["position"] = null,
        };
    }

    private static Dictionary<string, object?> Variables(object self, DislDiagram diagram, CelMap env) =>
        new(StringComparer.Ordinal) { ["self"] = self, ["diagram"] = diagram, ["env"] = env };

    private static Dictionary<string, object?> Connection(DislMenuTarget.OnConnection connection, CelMap env) =>
        new(StringComparer.Ordinal)
        {
            ["source"] = connection.Source, ["target"] = connection.Target, ["sourceAnchor"] = null, ["position"] = null, ["diagram"] = connection.Diagram, ["env"] = env,
        };

    private static Dictionary<string, object?> Also(this Dictionary<string, object?> variables, IReadOnlyDictionary<string, object?> more)
    {
        foreach (var (name, value) in more) variables[name] = value;
        return variables;
    }
}
