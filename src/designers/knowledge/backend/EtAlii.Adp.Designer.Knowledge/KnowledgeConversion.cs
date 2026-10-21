using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// One cell's value in a change of its property's type: what it holds, and the changes that
/// convert it, made in the same step as the change of type (<see cref="KnowledgeValueType.ConvertTo"/>).
/// </summary>
/// <param name="cellId">The cell.</param>
/// <param name="cell">What it holds: one or more values of the type the property had, under <paramref name="held"/>.</param>
/// <param name="held">The key the cell holds its value under now.</param>
/// <param name="names">The property's options' names, by their ids.</param>
/// <param name="option">The id of the option of a name, made in this step when the property has none of it.</param>
/// <param name="changes">Where the changes go, in order.</param>
internal sealed class KnowledgeConversion(string cellId, KnowledgeCell cell, string held, IReadOnlyDictionary<string, string> names, Func<string, string> option, List<ModelChange> changes)
{
    /// <summary>The values the cell holds.</summary>
    public IReadOnlyList<string> Values => cell.Values;

    /// <summary>The first value the cell holds.</summary>
    public string Value => cell.Values[0];

    /// <summary>The names of the property's options, by their ids.</summary>
    public IReadOnlyDictionary<string, string> Names => names;

    /// <summary>The id of the option of a name, made in this step when the property has none of it.</summary>
    public string Option(string name) => option(name);

    /// <summary>Takes the value from the key it is held under, and gives the cell these instead.</summary>
    public void Replace(params (string Key, object? Value)[] attributes) => Set([(held, null), .. attributes]);

    /// <summary>Sets attributes of the cell.</summary>
    public void Set(params (string Key, object? Value)[] attributes)
    {
        var set = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, object? value) in attributes)
        {
            set[key] = value;
        }

        changes.Add(new ModelChange.Set(cellId, set));
    }

    /// <summary>Adds an item holding one value to the cell.</summary>
    public void AddItem(string key, string value) =>
        changes.Add(new ModelChange.Add("CellItem", null, new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value }, cellId));

    /// <summary>Removes the cell's item holding this value.</summary>
    public void RemoveItem(string value) => changes.Add(new ModelChange.Remove($"{cellId}/{value}"));
}
