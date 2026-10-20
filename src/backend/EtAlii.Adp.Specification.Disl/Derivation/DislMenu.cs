namespace EtAlii.Adp.Specification.Disl;

/// <summary>A set of <c>toolbox.contextMenus</c> (DISL §7.3), typed at load.</summary>
/// <param name="DeclaresFor">Whether it states a <c>for</c>: a set without one is for every element.</param>
/// <param name="For">The types, or <c>diagram</c> or <c>connection</c>, it is for.</param>
/// <param name="When">Its condition, or null when it has none.</param>
/// <param name="Tools">Its entries, in order; none when they are not a list.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read a set that is not an object, null for one that is; such a
/// set still throws it where its <c>for</c> was read.
/// </param>
internal sealed record DislMenuSet(bool DeclaresFor, IReadOnlyList<string> For, DislExpressionField? When, IReadOnlyList<DislMenuTool> Tools, string? NotAnObject);

/// <summary>An entry of a context menu set (DISL §7.3), typed at load; every member absent from the definition is null or empty.</summary>
internal sealed record DislMenuTool
{
    public string? Group { get; init; }

    /// <summary>The list an entry is made for each item of, or null for one entry.</summary>
    public DislExpressionField? ForEach { get; init; }

    /// <summary>The name <see cref="ForEach"/> binds its item to besides <c>item</c>.</summary>
    public string? As { get; init; }

    public DislExpressionField? Visible { get; init; }

    /// <summary>Its <c>kind</c>, <c>operation</c> when it states none.</summary>
    public string Kind { get; init; } = "operation";

    public string? Operation { get; init; }

    /// <summary>Its <c>args</c>, Expressions, in order.</summary>
    public IReadOnlyList<(string Name, DislExpressionField Value)> Arguments { get; init; } = [];

    /// <summary>The relation type it connects through, whose label is the entry's when neither it nor its operation has one.</summary>
    public string? Via { get; init; }

    public DislMessage? Label { get; init; }

    public string? Icon { get; init; }

    public string? Shortcut { get; init; }

    public IReadOnlyList<DislReason> Unavailable { get; init; } = [];

    public DislExpressionField? Enabled { get; init; }

    /// <summary>
    /// The message System.Text.Json threw when the runtime read an entry that is not an object, null for one that is;
    /// such an entry still throws it where it was read.
    /// </summary>
    public string? NotAnObject { get; init; }
}
