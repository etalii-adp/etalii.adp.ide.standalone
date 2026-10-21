namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A relation: rows of a target table, by id, each an item of the cell. Shown by the related row's
/// title, and grouped a related row each. A property only becomes one by choosing its target, so
/// no other type converts to it.
/// </summary>
internal sealed class KnowledgeRelationType() : KnowledgeValueType("relation", "row", ["contains", "does-not-contain"])
{
    public override string CellKey => "rows";

    public override bool HoldsSeveral => true;

    public override bool CanGroup => true;

    public override string Shown(KnowledgeProperty property, string value, Func<string, string, string>? labelOf) => labelOf?.Invoke(property.Id, value) ?? "";

    protected override bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given) => HoldsAmong(comparison, values, given);

    /// <summary>Any related row is a group, whether or not the target still has it.</summary>
    public override bool TakesGroup(KnowledgeProperty grouped, string group) => true;

    /// <summary>A condition compares one related row at a time, by its id, written as text.</summary>
    public override string WriteCondition(string value, out string key, out object? written)
    {
        key = KnowledgeValueTypes.Text.StoredKey;
        written = value;
        return "";
    }
}
