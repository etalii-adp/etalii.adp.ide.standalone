using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A <see cref="DislChange"/> as the FBL change that writes it: the metamodel's types and attribute
/// names mapped back through <c>x-persistence.typeMap</c>, and each value in its stored form - a
/// <c>yearMonth</c> as <c>±YYYY-MM</c>, an enum value as its stored form, an element as its id.
/// </summary>
/// <remarks>
/// A <see cref="DislChange.Reparent"/> is written as a <see cref="ModelChange.Move"/>, which only a
/// persistence plugin writes: a declared binding refuses it with a sentence.
/// </remarks>
public static class DislWrite
{
    /// <summary>The FBL change that writes <paramref name="change"/>.</summary>
    public static ModelChange ToFbl(DislSpecification specification, DislChange change)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(change);
        var map = DislTypeMap.Of(specification);
        return change switch
        {
            DislChange.Create create => new ModelChange.Add(map.BindingTypeOf(create.Type), create.Id, Written(specification, map, create.Type, create.Attributes), create.ParentId),
            DislChange.Set set => new ModelChange.Set(set.ElementId, Written(specification, map, set.Type, set.Attributes)),
            DislChange.Remove remove => new ModelChange.Remove(remove.ElementId),
            DislChange.Reparent reparent => new ModelChange.Move(reparent.ElementId, reparent.ParentId, reparent.Index),
            _ => throw new ArgumentException($"{change.GetType().Name} is no change this runtime writes.", nameof(change)),
        };
    }

    /// <summary>The stored form of <paramref name="value"/> for <paramref name="attribute"/> of <paramref name="type"/>.</summary>
    public static object? StoredForm(DislSpecification specification, string type, string attribute, object? value)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return specification.Metamodel.TypeOf(type) is { } declared && declared.Attributes.TryGetValue(attribute, out var definition)
            ? Stored(specification, definition.Type, value)
            : value;
    }

    private static Dictionary<string, object?> Written(DislSpecification specification, DislTypeMap map, string type, IReadOnlyDictionary<string, object?> attributes) =>
        attributes.ToDictionary(
            attribute => map.BindingAttributeOf(type, attribute.Key),
            attribute => StoredForm(specification, type, attribute.Key, attribute.Value),
            StringComparer.Ordinal);

    private static object? Stored(DislSpecification specification, string type, object? value) => value switch
    {
        null => null,
        DislElement element => element.Id,
        IReadOnlyList<object?> list => list.Select(item => Stored(specification, type, item)).ToList(),
        long month when type == "yearMonth" => YearMonth.Format(month, "uuuu-MM"),
        string key when specification.Metamodel.Enums.TryGetValue(type, out var enumeration) => enumeration.ValueOf(key)?.Stored ?? key,
        _ => value,
    };
}
