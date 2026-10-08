namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The CEL contexts of DISL §12.3 and the gesture contexts of §8.4: the variables an expression in
/// each may read. An expression is compiled in exactly one of them, so a variable its context does not
/// bind is refused at load, naming the variables it does.
/// </summary>
public static class DislContexts
{
    public const string Element = "element";
    public const string CompartmentItem = "compartmentItem";
    public const string Shape = "shape";
    public const string Marker = "marker";
    public const string Handle = "handle";
    public const string HandleSnap = "handleSnap";
    public const string HandleWrite = "handleWrite";
    public const string Placement = "placement";
    public const string PlacementWrite = "placementWrite";
    public const string Snap = "snap";
    public const string Categories = "categories";
    public const string Constraint = "constraint";
    public const string BuiltInMessage = "constraint+detail";
    public const string BuiltInRefusal = "refusal";
    public const string Identity = "identity";
    public const string Create = "create";
    public const string Form = "form";
    public const string Hook = "hook";
    public const string Operation = "operation";
    public const string Derive = "derive";
    public const string DeriveItem = "deriveItem";
    public const string Filter = "filter";
    public const string FilterOptions = "filterOptions";
    public const string Legend = "legend";
    public const string Chrome = "chrome";
    public const string Budget = "budget";
    public const string Connection = "connection";
    public const string Simulation = "simulation";
    private const string Template = "template";
    public const string Migration = "migration";
    public const string Retype = "retype";
    public const string LabelParse = "labelParse";
    public const string DataTypeDisplay = "dataTypeDisplay";
    private const string GestureConnect = "gesture:connect";
    private const string GestureContainment = "gesture:containment";
    private const string GestureCreate = "gesture:create";
    public const string GestureDelete = "gesture:delete";
    public const string GesturePlacement = "gesture:placement";
    public const string GestureChange = "gesture:change";
    private const string GestureReorder = "gesture:reorder";

    private static readonly string[] Shaped = ["w", "h", "p", "self", "env"];

    private static readonly Dictionary<string, string[]> _variables = new(StringComparer.Ordinal)
    {
        [Element] = ["self", "diagram", "env"],
        [CompartmentItem] = ["self", "item", "index", "diagram", "env"],
        [Shape] = Shaped,
        [Marker] = [.. Shaped, "sw"],
        [Handle] = [.. Shaped, "px", "py"],
        // A handle's snap is a CEL snap rule (§6.8) in the handle context: the snap rule's value, axis and zoom besides.
        [HandleSnap] = [.. Shaped, "px", "py", "value", "axis", "zoom", "diagram"],
        [HandleWrite] = ["self", "diagram", "env", "p", "w", "h", "value", "yValue"],
        [Placement] = ["self", "diagram", "env", "parent", "axis"],
        [PlacementWrite] = ["self", "diagram", "env", "parent", "axis", "value"],
        [Snap] = ["value", "axis", "zoom", "self", "parent", "diagram"],
        [Categories] = ["diagram", "env"],
        [Constraint] = ["self", "diagram", "env"],
        [BuiltInMessage] = ["self", "diagram", "env", "detail"],
        // A built-in's refusal (§8.4) sees the gesture's variables plus violation; the union of the gesture kinds a built-in refuses.
        [BuiltInRefusal] = ["self", "diagram", "env", "violation", "relationType", "source", "target", "sourcePort", "targetPort", "child", "parent", "slot", "attribute", "oldValue", "newValue", "oldBounds", "newBounds", "newParent", "gesture"],
        [Identity] = ["self", "diagram"],
        [Create] = ["diagram", "env", "parent", "elementType", "position", "dropTarget", "tool"],
        [Form] = ["self", "value", "diagram", "env"],
        [Hook] = ["self", "old", "event", "diagram", "env"],
        [Operation] = ["self", "selection", "p", "diagram", "env", "position"],
        [Derive] = ["diagram", "env"],
        [DeriveItem] = ["item", "group", "index", "diagram", "env"],
        [Filter] = ["self", "value", "match", "diagram", "env"],
        [FilterOptions] = ["value", "match", "diagram", "env"],
        [Legend] = ["self", "diagram", "env"],
        [Chrome] = ["diagram", "env"],
        [Budget] = ["self", "index", "diagram", "env"],
        [Connection] = ["source", "target", "sourceAnchor", "position", "diagram", "env"],
        [Simulation] = ["self", "state", "step", "p", "diagram", "env"],
        [Template] = ["p", "diagram", "env", "refs"],
        [Migration] = ["element", "value", "document", "from", "to"],
        // A retype's attribute mapping (§9.5) is an Expression over the element as it was.
        [Retype] = ["self", "old", "diagram", "env"],
        // A label's parse (§6.12): the element's context with value bound to the entered text.
        [LabelParse] = ["self", "diagram", "env", "value"],
        [DataTypeDisplay] = ["self", "diagram", "env", "value"],
        [GestureConnect] = ["relationType", "source", "target", "sourcePort", "targetPort", "self", "diagram", "env"],
        [GestureContainment] = ["child", "parent", "slot", "diagram", "env"],
        [GestureCreate] = ["elementType", "parent", "position", "dropTarget", "tool", "diagram", "env"],
        [GestureDelete] = ["self", "selection", "diagram", "env"],
        [GesturePlacement] = ["self", "oldBounds", "newBounds", "newParent", "gesture", "diagram", "env"],
        [GestureChange] = ["self", "attribute", "oldValue", "newValue", "diagram", "env"],
        [GestureReorder] = ["self", "parent", "oldIndex", "newIndex", "siblings", "diagram", "env"],
    };

    /// <summary>Every context this runtime knows.</summary>
    public static IEnumerable<string> All => _variables.Keys;

    /// <summary>The variables of <paramref name="context"/>.</summary>
    public static IReadOnlyList<string> VariablesOf(string context) =>
        _variables.TryGetValue(context, out var variables) ? variables : throw new ArgumentException($"There is no CEL context '{context}'.", nameof(context));

    /// <summary>The gesture context of a constraint of <paramref name="kind"/> (§8.4), or <see cref="Constraint"/> for an invariant.</summary>
    public static string OfConstraintKind(string? kind) => kind switch
    {
        null or "invariant" => Constraint,
        "connect" => GestureConnect,
        "containment" => GestureContainment,
        "create" => GestureCreate,
        "delete" => GestureDelete,
        "placement" => GesturePlacement,
        "change" => GestureChange,
        "reorder" => GestureReorder,
        _ => Constraint,
    };
}
