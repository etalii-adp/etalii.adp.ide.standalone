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

    internal sealed record OnCanvas(DislDiagram Diagram) : DislMenuTarget(Diagram);

    internal sealed record OnConnection(DislDiagram Diagram, DislElement? Source, DislElement? Target, string? RelationType) : DislMenuTarget(Diagram);
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

    /// <summary>
    /// <see cref="Key"/> as a browser's <c>KeyboardEvent.key</c> spells it, which is how a host sends and
    /// matches a keypress: <c>Space</c> is <c>" "</c>, <c>Up</c> is <c>ArrowUp</c>, <c>Esc</c> is
    /// <c>Escape</c>; every other key is spelled the same.
    /// </summary>
    public string EventKey => Key switch
    {
        "Space" => " ",
        "Up" => "ArrowUp",
        "Down" => "ArrowDown",
        "Left" => "ArrowLeft",
        "Right" => "ArrowRight",
        "Esc" => "Escape",
        _ => Key,
    };
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

/// <summary>One group of a context menu: consecutive entries with one <c>group</c> name (null when they state none).</summary>
public sealed record DerivedMenuGroup(string? Name, IReadOnlyList<DerivedMenuEntry> Entries);

/// <summary>
/// The context menu of a target (DISL §7.3): the matching sets of <c>toolbox.contextMenus</c> whose
/// <c>when</c> holds, in declaration order; each entry once, or once per item of its <c>forEach</c>,
/// unless its <c>visible</c> fails; and each entry's availability from its own <c>unavailable</c>
/// reasons, then its operation's, then <c>enabled</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Groups</b> follow <c>group</c> (DISL 0.3 §7.3): consecutive entries of
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
        (DislContext context, Dictionary<string, object?> variables, IReadOnlyList<string> types) = target switch
        {
            DislMenuTarget.OnElement element => (DislContext.Element, Variables(element.Self, target.Diagram, cel), element.Self.Type.Linearisation),
            DislMenuTarget.OnConnection connection => (DislContext.Connection, Connection(connection, cel), (IReadOnlyList<string>)["connection"]),
            _ => (DislContext.Element, Variables(target.Diagram, target.Diagram, cel), (IReadOnlyList<string>)["diagram"]),
        };

        var toolbox = specification.Toolbox;
        if (toolbox.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);

        var matched = false;
        foreach (var set in toolbox.ContextMenus)
        {
            if (!Applies(set, target)) continue;
            matched = true;
            if (set.When is { } when && !when.Holds(variables)) continue;
            Set(specification, set, context, variables, types, target, ids, groups);
        }

        if (!matched && target is DislMenuTarget.OnConnection { RelationType: { } relation } pending)
        {
            ConnectEdit(specification, pending, relation, cel, ids, groups);
        }
        return groups;
    }

    private static bool Applies(DislMenuSet set, DislMenuTarget target)
    {
        if (set.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        return target switch
        {
            DislMenuTarget.OnElement element => !set.DeclaresFor || set.For.Any(name => name is not ("diagram" or "connection") && element.Self.IsA(name)),
            DislMenuTarget.OnCanvas => set.For.Contains("diagram"),
            DislMenuTarget.OnConnection => set.For.Contains("connection"),
            _ => false,
        };
    }

    private static void Set(
        DislSpecification specification,
        DislMenuSet set,
        DislContext context,
        Dictionary<string, object?> variables,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids,
        List<DerivedMenuGroup> groups)
    {
        List<DerivedMenuEntry>? current = null;
        string? currentName = null;
        foreach (var tool in set.Tools)
        {
            if (current is null || tool.Group != currentName)
            {
                if (current is { Count: > 0 }) groups.Add(new DerivedMenuGroup(currentName, current));
                current = [];
                currentName = tool.Group;
            }
            current.AddRange(Entries(specification, tool, context, variables, types, target, ids));
        }
        if (current is { Count: > 0 }) groups.Add(new DerivedMenuGroup(currentName, current));
    }

    private static IEnumerable<DerivedMenuEntry> Entries(
        DislSpecification specification,
        DislMenuTool tool,
        DislContext context,
        Dictionary<string, object?> variables,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids)
    {
        if (tool.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (tool.ForEach is not { } forEach)
        {
            if (Entry(specification, tool, context, variables, new Dictionary<string, object?>(), types, target, ids) is { } entry) yield return entry;
            yield break;
        }

        // An entry per item, in list order, with item (or its as name) and index bound (§7.3); an empty or failing list gives none.
        if (forEach.Evaluate(variables) is not IReadOnlyList<object?> items) yield break;
        for (var index = 0; index < items.Count; index++)
        {
            var bindings = new Dictionary<string, object?>(StringComparer.Ordinal) { ["item"] = items[index], ["index"] = (long)index };
            if (tool.As is { } named) bindings[named] = items[index];
            if (Entry(specification, tool, context, variables, bindings, types, target, ids) is { } entry) yield return entry;
        }
    }

    private static DerivedMenuEntry? Entry(
        DislSpecification specification,
        DislMenuTool tool,
        DislContext context,
        Dictionary<string, object?> outer,
        Dictionary<string, object?> bindings,
        IReadOnlyList<string> types,
        DislMenuTarget target,
        WireIdMap ids)
    {
        var variables = bindings.Count == 0 ? outer : new Dictionary<string, object?>(outer).Also(bindings);
        if (tool.Visible is { } visible && !visible.Holds(variables)) return null;

        var kind = tool.Kind;
        var operation = Operation(specification, tool.Operation);

        var arguments = new Dictionary<string, object?>(bindings, StringComparer.Ordinal);
        foreach ((string name, DislExpressionField value) in tool.Arguments) arguments[name] = value.Evaluate(variables);

        var fallback = operation?.Label is { } operationLabel
            ? operationLabel.Text(Operational(target, variables, arguments), kind)
            : tool.Via is { } via && specification.Metamodel.TypeOf(via) is { } viaType
                ? DislJson.String(viaType.Json, "label") ?? via
                : kind;
        var label = tool.Label?.Text(variables, fallback) ?? fallback;
        var icon = tool.Icon ?? operation?.Icon ?? "";
        var shortcut = tool.Shortcut ?? operation?.Shortcut;

        var reason = Unavailable(specification, tool, context, variables, operation, target, arguments);
        var available = reason is null && (tool.Enabled is not { } enabled || enabled.Holds(variables));

        return new DerivedMenuEntry(
            ids.ActionId(types, kind, tool.Operation, arguments),
            kind,
            tool.Operation,
            label,
            icon,
            shortcut is null ? null : DerivedShortcut.Parse(shortcut),
            available,
            reason ?? "",
            arguments);
    }

    private static string? Unavailable(
        DislSpecification specification,
        DislMenuTool tool,
        DislContext context,
        IReadOnlyDictionary<string, object?> variables,
        DislOperation? operation,
        DislMenuTarget target,
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (DislReason.First(tool.Unavailable, specification, variables) is { } reason) return reason;
        if (operation is { Unavailable.Count: > 0 }
            && DislReason.First(operation.Unavailable, specification, Operational(target, variables, arguments)) is { } inheritedReason)
        {
            return inheritedReason;
        }
        if (tool.Kind is "moveUp" or "moveDown" && target is DislMenuTarget.OnElement { Self: { } self })
        {
            var siblings = self.Parent?.Children ?? [.. self.Diagram.Nodes.Where(node => node.Parent is null)];
            var place = siblings.ToList().FindIndex(sibling => ReferenceEquals(sibling, self));
            if (tool.Kind == "moveUp" && place <= 0) return DislEvaluation.StandardMessage(specification, "std.atStart", variables, "It is already the first.");
            if (tool.Kind == "moveDown" && place >= siblings.Count - 1) return DislEvaluation.StandardMessage(specification, "std.atEnd", variables, "It is already the last.");
        }
        return null;
    }

    /// <summary>The menu of a connection on a derived relation type with a <c>connect</c> edit (§4.11): its operation, as one entry.</summary>
    private static void ConnectEdit(DislSpecification specification, DislMenuTarget.OnConnection pending, string relation, CelMap env, WireIdMap ids, List<DerivedMenuGroup> groups)
    {
        if (specification.Metamodel.TypeOf(relation)?.ConnectEdit is not { } operationName) return;
        if (Operation(specification, operationName) is not { } operation) return;

        // The relation does not exist yet: self is its two ends, as the operation's reasons read them.
        var self = new CelMap { ["source"] = pending.Source, ["target"] = pending.Target, ["type"] = relation };
        var variables = new Dictionary<string, object?>
        {
            ["self"] = self, ["selection"] = new List<object?>(), ["p"] = new CelMap(), ["diagram"] = pending.Diagram, ["env"] = env, ["position"] = null,
        };
        var label = operation.Label?.Text(variables, operationName) ?? operationName;
        var reason = DislReason.First(operation.Unavailable, specification, variables);
        var entry = new DerivedMenuEntry(
            ids.ActionId([relation, "connection"], "connect", operationName, new Dictionary<string, object?>()),
            "operation",
            operationName,
            label,
            operation.Icon ?? "",
            operation.Shortcut is { } shortcut ? DerivedShortcut.Parse(shortcut) : null,
            reason is null,
            reason ?? "",
            new Dictionary<string, object?>());
        groups.Add(new DerivedMenuGroup(null, [entry]));
    }

    /// <summary>The operation <paramref name="name"/> a menu entry runs; none for an operation that is not an object, whose members a menu reads as absent.</summary>
    private static DislOperation? Operation(DislSpecification specification, string? name) =>
        name is not null && specification.Operations.Find(name) is { NotAnObject: null } operation ? operation : null;

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
        foreach ((string name, object? value) in arguments) p[name] = value;
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
        foreach ((string name, object? value) in more) variables[name] = value;
        return variables;
    }
}
