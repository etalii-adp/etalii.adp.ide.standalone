using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The toolbox of a specification (DISL §7.1 to §7.3), typed at load: its palette, its library of shared tools and its
/// context menus.
/// </summary>
/// <param name="Palette">Every entry of every group, nested groups depth first, in declaration order.</param>
/// <param name="Library">The tools of <c>toolbox.tools</c> by name, in declaration order; null when it states none or they are not an object.</param>
/// <param name="ContextMenus">The sets of <c>toolbox.contextMenus</c>, in order; none when they are not a list.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read a member of a toolbox that is not an object, null for one
/// that is; such a toolbox has no palette, and still throws it where its members were read.
/// </param>
internal sealed record DislToolbox(
    IReadOnlyList<DislPaletteItem> Palette,
    IReadOnlyList<(string Name, DislTool Tool)>? Library,
    IReadOnlyList<DislMenuSet> ContextMenus,
    string? NotAnObject)
{
    public static DislToolbox None { get; } = new([], null, [], null);

    /// <summary>
    /// The tool <paramref name="id"/> as the palette declares it, inline in a group or by reference from the library,
    /// the first in palette order; null when there is none. What the runtime threw on before it found the tool, it
    /// still throws.
    /// </summary>
    public DislTool? Find(string id)
    {
        if (NotAnObject is not null) throw new InvalidOperationException(NotAnObject);
        foreach (var item in Palette)
        {
            switch (item)
            {
                case DislPaletteItem.Malformed malformed when malformed.Name is null || malformed.Name == id:
                    throw new InvalidOperationException(malformed.Message);
                case DislPaletteItem.Entry entry when entry.Key == id:
                    return entry.Tool;
            }
        }
        return null;
    }
}

/// <summary>One place in the palette (DISL §7.1), typed at load.</summary>
internal abstract record DislPaletteItem
{
    /// <summary>A tool of the group <paramref name="Group"/>, found by <paramref name="Key"/>: the name a reference gives, else the inline tool's own <c>id</c>.</summary>
    public sealed record Entry(string Group, string? Key, DislTool Tool) : DislPaletteItem;

    /// <summary>
    /// A group that is not an object (<paramref name="Name"/> null), or a reference named <paramref name="Name"/> into
    /// a library that is not an object: the runtime threw <paramref name="Message"/> where it read it.
    /// </summary>
    public sealed record Malformed(string? Name, string Message) : DislPaletteItem;
}

/// <summary>A palette tool (DISL §7.1, §7.2), typed at load; every member absent from the definition is null or empty.</summary>
internal sealed record DislTool
{
    /// <summary>Its own <c>id</c>.</summary>
    public string? Id { get; init; }

    /// <summary>The type it creates.</summary>
    public string? Creates { get; init; }

    public DislMessage? Label { get; init; }

    public string? Icon { get; init; }

    /// <summary>Its <c>doc</c> as one text: a string, or an object's summary; empty without one.</summary>
    public string Doc { get; init; } = "";

    /// <summary>Its <c>kind</c> and <c>operation</c>, which a tool without <c>drop</c> is dropped by (DISL 0.1).</summary>
    public string? Kind { get; init; }

    public string? Operation { get; init; }

    /// <summary>What follows a drop: <c>select</c> or <c>editLabel</c>.</summary>
    public string? After { get; init; }

    public IReadOnlyList<DislToolInitial> Initial { get; init; } = [];

    /// <summary>The end a connect gesture released on empty canvas creates (§6.10), or null when it states none.</summary>
    public DislToolEnd? CreateSource { get; init; }

    public DislToolEnd? CreateTarget { get; init; }

    /// <summary>Its <c>drop</c>, or null when it has none or that is not an object.</summary>
    public DislToolDrop? Drop { get; init; }

    /// <summary>
    /// The message System.Text.Json threw when the runtime read a member of a library tool that is not an object, null
    /// for one that is; such a tool creates nothing, and still throws it where its members were read.
    /// </summary>
    public string? NotAnObject { get; init; }
}

/// <summary>
/// One <c>initial</c> value of a tool or a created end: an object is an Expression in the create context, any other
/// value is read as the attribute it names when the tool runs, as an attribute's default is.
/// </summary>
internal sealed record DislToolInitial(string Name, DislExpressionField? Expression, JsonElement Literal);

/// <summary>A <c>createSource</c> or <c>createTarget</c> (§6.10): the type to create, unless it says <c>ask</c>, and its initial values.</summary>
internal sealed record DislToolEnd(string? Type, bool Ask, IReadOnlyList<DislToolInitial> Initial);

/// <summary>A tool's <c>drop</c> (§7.2): its <c>place</c>, its targets that are objects, what happens elsewhere, and its refusal.</summary>
/// <param name="Refusal">Its <c>refusal</c> Reasons, one for a refusal that is not a list; null when it states none.</param>
internal sealed record DislToolDrop(string? Place, IReadOnlyList<DislDropTarget> Targets, string Elsewhere, IReadOnlyList<DislReason>? Refusal);

/// <summary>One drop target (§7.2): a context tool plus the types it is <c>on</c>; its <c>args</c> are Expressions.</summary>
internal sealed record DislDropTarget(IReadOnlyList<string> On, string Kind, string? Operation, IReadOnlyList<(string Name, DislExpressionField Value)> Arguments);
