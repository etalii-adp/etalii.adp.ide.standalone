using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Sets one key of one entry - one of <see cref="SankeyKeys"/> - to <paramref name="Value"/>.</summary>
/// <remarks>
/// One command for every scalar edit, because each is the same act - one key of one entry - and
/// differs only in how its value is checked, which <see cref="SankeyKeys"/> states once.
/// </remarks>
public sealed record SetSankeyPropertyCommand(string BodyPath, string EntryId, string Key, string Value) : ICommand;

/// <summary>The keys an edit may set, as the document spells them.</summary>
public static class SankeyKeys
{
    public const string Name = "name";
    public const string Color = "color";
    public const string Note = "note";
    public const string Format = "format";
    public const string Column = "column";
    public const string Description = "description";
    public const string Value = "value";
    public const string Step = "step";
}
