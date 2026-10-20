namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The CEL contexts of DISL §12.3 and the gesture contexts of §8.4: the variables an expression in
/// each may read, and the name a diagnostic gives each. An expression is compiled in exactly one of
/// them, so a variable its context does not bind is refused at load, naming the variables it does.
/// </summary>
public static class DislContexts
{
    private static readonly string[] Shaped = ["w", "h", "p", "self", "env"];

    /// <summary>A context's name, as a diagnostic gives it, and its variables.</summary>
    private sealed record Declared(string Name, string[] Variables);

    private static readonly Dictionary<DislContext, Declared> _contexts = new()
    {
        [DislContext.Element] = new("element", ["self", "diagram", "env"]),
        [DislContext.CompartmentItem] = new("compartmentItem", ["self", "item", "index", "diagram", "env"]),
        [DislContext.Shape] = new("shape", Shaped),
        [DislContext.Marker] = new("marker", [.. Shaped, "sw"]),
        [DislContext.Handle] = new("handle", [.. Shaped, "px", "py"]),
        // A handle's snap is a CEL snap rule (§6.8) in the handle context: the snap rule's value, axis and zoom besides.
        [DislContext.HandleSnap] = new("handleSnap", [.. Shaped, "px", "py", "value", "axis", "zoom", "diagram"]),
        [DislContext.HandleWrite] = new("handleWrite", ["self", "diagram", "env", "p", "w", "h", "value", "yValue"]),
        [DislContext.Placement] = new("placement", ["self", "diagram", "env", "parent", "axis"]),
        [DislContext.PlacementWrite] = new("placementWrite", ["self", "diagram", "env", "parent", "axis", "value"]),
        [DislContext.Snap] = new("snap", ["value", "axis", "zoom", "self", "parent", "diagram"]),
        [DislContext.Categories] = new("categories", ["diagram", "env"]),
        [DislContext.Constraint] = new("constraint", ["self", "diagram", "env"]),
        [DislContext.BuiltInMessage] = new("constraint+detail", ["self", "diagram", "env", "detail"]),
        // A built-in's refusal (§8.4) sees the gesture's variables plus violation; the union of the gesture kinds a built-in refuses.
        [DislContext.BuiltInRefusal] = new("refusal", ["self", "diagram", "env", "violation", "relationType", "source", "target", "sourcePort", "targetPort", "child", "parent", "slot", "attribute", "oldValue", "newValue", "oldBounds", "newBounds", "newParent", "gesture"]),
        [DislContext.Identity] = new("identity", ["self", "diagram"]),
        [DislContext.Create] = new("create", ["diagram", "env", "parent", "elementType", "position", "dropTarget", "tool"]),
        [DislContext.Form] = new("form", ["self", "value", "diagram", "env"]),
        [DislContext.Hook] = new("hook", ["self", "old", "event", "diagram", "env"]),
        [DislContext.Operation] = new("operation", ["self", "selection", "p", "diagram", "env", "position"]),
        [DislContext.Derive] = new("derive", ["diagram", "env"]),
        [DislContext.DeriveItem] = new("deriveItem", ["item", "group", "index", "diagram", "env"]),
        [DislContext.Filter] = new("filter", ["self", "value", "match", "diagram", "env"]),
        [DislContext.FilterOptions] = new("filterOptions", ["value", "match", "diagram", "env"]),
        [DislContext.Legend] = new("legend", ["self", "diagram", "env"]),
        [DislContext.Chrome] = new("chrome", ["diagram", "env"]),
        [DislContext.Budget] = new("budget", ["self", "index", "diagram", "env"]),
        [DislContext.Connection] = new("connection", ["source", "target", "sourceAnchor", "position", "diagram", "env"]),
        [DislContext.Simulation] = new("simulation", ["self", "state", "step", "p", "diagram", "env"]),
        [DislContext.Template] = new("template", ["p", "diagram", "env", "refs"]),
        [DislContext.Migration] = new("migration", ["element", "value", "document", "from", "to"]),
        // A retype's attribute mapping (§9.5) is an Expression over the element as it was.
        [DislContext.Retype] = new("retype", ["self", "old", "diagram", "env"]),
        // A label's parse (§6.12): the element's context with value bound to the entered text.
        [DislContext.LabelParse] = new("labelParse", ["self", "diagram", "env", "value"]),
        [DislContext.DataTypeDisplay] = new("dataTypeDisplay", ["self", "diagram", "env", "value"]),
        [DislContext.GestureConnect] = new("gesture:connect", ["relationType", "source", "target", "sourcePort", "targetPort", "self", "diagram", "env"]),
        [DislContext.GestureContainment] = new("gesture:containment", ["child", "parent", "slot", "diagram", "env"]),
        [DislContext.GestureCreate] = new("gesture:create", ["elementType", "parent", "position", "dropTarget", "tool", "diagram", "env"]),
        [DislContext.GestureDelete] = new("gesture:delete", ["self", "selection", "diagram", "env"]),
        [DislContext.GesturePlacement] = new("gesture:placement", ["self", "oldBounds", "newBounds", "newParent", "gesture", "diagram", "env"]),
        [DislContext.GestureChange] = new("gesture:change", ["self", "attribute", "oldValue", "newValue", "diagram", "env"]),
        [DislContext.GestureReorder] = new("gesture:reorder", ["self", "parent", "oldIndex", "newIndex", "siblings", "diagram", "env"]),
    };

    /// <summary>Every context this runtime knows.</summary>
    public static IEnumerable<DislContext> All => _contexts.Keys;

    /// <summary>The variables of <paramref name="context"/>.</summary>
    public static IReadOnlyList<string> VariablesOf(DislContext context) => Of(context).Variables;

    /// <summary>The name of <paramref name="context"/> as a diagnostic gives it: §12.3's, such as <c>element</c> or <c>gesture:connect</c>.</summary>
    public static string NameOf(DislContext context) => Of(context).Name;

    private static Declared Of(DislContext context) =>
        _contexts.TryGetValue(context, out var declared) ? declared : throw new ArgumentException($"There is no CEL context '{context}'.", nameof(context));

    /// <summary>The gesture context of a constraint of <paramref name="kind"/> (§8.4), or <see cref="DislContext.Constraint"/> for an invariant.</summary>
    public static DislContext OfConstraintKind(string? kind) => kind switch
    {
        null or "invariant" => DislContext.Constraint,
        "connect" => DislContext.GestureConnect,
        "containment" => DislContext.GestureContainment,
        "create" => DislContext.GestureCreate,
        "delete" => DislContext.GestureDelete,
        "placement" => DislContext.GesturePlacement,
        "change" => DislContext.GestureChange,
        "reorder" => DislContext.GestureReorder,
        _ => DislContext.Constraint,
    };
}
