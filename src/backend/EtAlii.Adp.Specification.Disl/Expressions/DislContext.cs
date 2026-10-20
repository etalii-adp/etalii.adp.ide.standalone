namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Where a CEL expression of a specification appears, which decides the variables it may read: the
/// contexts of DISL §12.3 and the gesture contexts of §8.4. <see cref="DislContexts"/> holds each one's
/// variables and the name a diagnostic gives it.
/// </summary>
public enum DislContext
{
    /// <summary><c>element</c>: an element's own expressions, and the default of most sections.</summary>
    Element,

    /// <summary><c>compartmentItem</c>: what a compartment says per item (§6.9).</summary>
    CompartmentItem,

    /// <summary><c>shape</c>: the GeomExprs of a shape (§6.8).</summary>
    Shape,

    /// <summary><c>marker</c>: the GeomExprs of a marker, which sees the stroke width besides a shape's.</summary>
    Marker,

    /// <summary><c>handle</c>: a handle's label and values (§6.8).</summary>
    Handle,

    /// <summary><c>handleSnap</c>: a handle's snap rule (§6.8).</summary>
    HandleSnap,

    /// <summary><c>handleWrite</c>: a handle's write actions (§6.8).</summary>
    HandleWrite,

    /// <summary><c>placement</c>: the coordinates section.</summary>
    Placement,

    /// <summary><c>placementWrite</c>: a placement's write actions.</summary>
    PlacementWrite,

    /// <summary><c>snap</c>: a snapping rule.</summary>
    Snap,

    /// <summary><c>categories</c>: the categories of a coordinate axis.</summary>
    Categories,

    /// <summary><c>constraint</c>: an invariant rule (§8.1).</summary>
    Constraint,

    /// <summary><c>constraint+detail</c>: a built-in constraint's message and code, which see its detail.</summary>
    BuiltInMessage,

    /// <summary><c>refusal</c>: a built-in constraint's refusal of a gesture (§8.4).</summary>
    BuiltInRefusal,

    /// <summary><c>identity</c>: a derived id (§11.5.2).</summary>
    Identity,

    /// <summary><c>create</c>: what is evaluated as an element is created or dropped.</summary>
    Create,

    /// <summary><c>form</c>: a form's expressions (§7.5).</summary>
    Form,

    /// <summary><c>hook</c>: a hook's actions (§9.2).</summary>
    Hook,

    /// <summary><c>operation</c>: an operation's actions (§9.3).</summary>
    Operation,

    /// <summary><c>derive</c>: the <c>from</c> of a derived type (§4.11).</summary>
    Derive,

    /// <summary><c>deriveItem</c>: every other expression of a derived type (§4.11).</summary>
    DeriveItem,

    /// <summary><c>filter</c>: a notation filter.</summary>
    Filter,

    /// <summary><c>filterOptions</c>: a filter's options.</summary>
    FilterOptions,

    /// <summary><c>legend</c>: a legend.</summary>
    Legend,

    /// <summary><c>chrome</c>: the canvas's notices, chrome and empty-canvas text.</summary>
    Chrome,

    /// <summary><c>budget</c>: a language budget (§3.2.1).</summary>
    Budget,

    /// <summary><c>connection</c>: a context menu for a pending connection (§7.3).</summary>
    Connection,

    /// <summary><c>simulation</c>: the initial, next and until of a simulation (§9.6).</summary>
    Simulation,

    /// <summary><c>template</c>: a template's fragment (§7.4).</summary>
    Template,

    /// <summary><c>migration</c>: a persistence migration.</summary>
    Migration,

    /// <summary><c>retype</c>: a retype's attribute mapping (§9.5).</summary>
    Retype,

    /// <summary><c>labelParse</c>: a label's parse (§6.12).</summary>
    LabelParse,

    /// <summary><c>dataTypeDisplay</c>: a data type's display.</summary>
    DataTypeDisplay,

    /// <summary><c>gesture:connect</c>: a constraint of kind <c>connect</c> (§8.4).</summary>
    GestureConnect,

    /// <summary><c>gesture:containment</c>: a constraint of kind <c>containment</c> (§8.4).</summary>
    GestureContainment,

    /// <summary><c>gesture:create</c>: a constraint of kind <c>create</c> (§8.4).</summary>
    GestureCreate,

    /// <summary><c>gesture:delete</c>: a constraint of kind <c>delete</c>, and the deletion policy (§8.4, §9.5).</summary>
    GestureDelete,

    /// <summary><c>gesture:placement</c>: a constraint of kind <c>placement</c> (§8.4).</summary>
    GesturePlacement,

    /// <summary><c>gesture:change</c>: a constraint of kind <c>change</c> (§8.4).</summary>
    GestureChange,

    /// <summary><c>gesture:reorder</c>: a constraint of kind <c>reorder</c> (§8.4).</summary>
    GestureReorder,
}
