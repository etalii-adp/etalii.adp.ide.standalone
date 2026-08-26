namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The DSL's three positioned statement kinds (Requirement 5.1).
/// </summary>
/// <remarks>
/// Three, not five. `market` and `ecosystem` read like kinds and are not: the real parser
/// rejects `market Foo [0.6, 0.8]` and accepts `component Foo [0.6, 0.8] (market)`, carrying
/// both in the same decorator set as build, buy and outsource. They are
/// <see cref="WardleyDecorator"/>s, and Requirement 6.3 owns them.
/// </remarks>
public enum WardleyElementKind
{
    /// <summary>An ordinary component of the value chain.</summary>
    Component,

    /// <summary>The user need the chain hangs from, at the top of the visibility axis.</summary>
    Anchor,

    /// <summary>A reference to another map, which activating opens (Requirement 6.6).</summary>
    Submap,
}
