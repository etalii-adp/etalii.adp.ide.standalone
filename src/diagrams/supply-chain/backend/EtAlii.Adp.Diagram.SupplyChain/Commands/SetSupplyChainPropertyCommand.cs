using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Sets one key of one entry - one of <see cref="SupplyChainKeys"/> - to <paramref name="Value"/>.</summary>
/// <remarks>
/// One command for every scalar edit, because each is the same act - one key of one entry - and
/// differs only in how its value is checked, which <see cref="SupplyChainKeys"/> states once.
/// </remarks>
public sealed record SetSupplyChainPropertyCommand(string BodyPath, string EntryId, string Key, string Value) : ICommand;

/// <summary>The keys an edit may set, as the document spells them.</summary>
public static class SupplyChainKeys
{
    public const string Name = "name";
    public const string Description = "description";
    public const string Type = "type";
    public const string Group = "group";
    public const string Quantity = "quantity";
    public const string Unit = "unit";
    public const string Step = "step";
    public const string Product = "product";
    public const string Volume = "volume";

    /// <summary>The keys that hold a number.</summary>
    public static readonly IReadOnlyList<string> Numbers = [Quantity, Step, Volume];
}
