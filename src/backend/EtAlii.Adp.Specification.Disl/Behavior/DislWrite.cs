using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A <see cref="DislChange"/> as the FBL change that writes it: the metamodel's types and attribute
/// names mapped back through <c>persistence.typeMap</c>, each value in its stored form - a
/// <c>yearMonth</c> as <c>±YYYY-MM</c>, an enum value as its stored form, an element as its id - and
/// every id as the binding reads it.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="DislChange.Reparent"/> is written as a <see cref="ModelChange.Move"/> and a
/// <see cref="DislChange.Retype"/> as a <see cref="ModelChange.Retype"/>, which only a persistence
/// plugin writes: a declared binding refuses them with a sentence.
/// </para>
/// <para>
/// <b>An id is written as it was read.</b> Under the <c>natural</c> strategy an element's id is the
/// prefix its type declares followed by the id the binding read (§11.5.1, <see cref="DislModelBuilder"/>);
/// the binding knows only the latter, so the prefix is taken off again, wherever an id is written: the
/// element changed, a relation's ends, and an attribute that holds elements.
/// </para>
/// <para>
/// <b>A <see cref="DislChange.Connect"/> is written as an add of the relation</b>, with its ends in the
/// attributes <c>persistence.typeMap</c> maps to <c>source</c> and <c>target</c>, or as <c>source</c>
/// and <c>target</c> where it maps none.
/// </para>
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
            DislChange.Create create => new ModelChange.Add(
                map.BindingTypeOf(create.Type),
                ReadId(specification, create.Type, create.Id),
                Written(specification, map, create.Type, create.Attributes),
                create.ParentId is { } parent ? ReadId(specification, parent) : null),
            DislChange.Connect connect => new ModelChange.Add(map.BindingTypeOf(connect.Type), ReadId(specification, connect.Type, connect.Id), Ends(specification, map, connect)),
            DislChange.Set set => new ModelChange.Set(ReadId(specification, set.Type, set.ElementId), Written(specification, map, set.Type, set.Attributes)),
            DislChange.Remove remove => new ModelChange.Remove(ReadId(specification, remove.ElementId)),
            DislChange.Reparent reparent => new ModelChange.Move(ReadId(specification, reparent.ElementId), reparent.ParentId is { } parent ? ReadId(specification, parent) : null, reparent.Index),
            DislChange.Retype retype => new ModelChange.Retype(ReadId(specification, retype.ElementId), map.BindingTypeOf(retype.Type), Written(specification, map, retype.Type, retype.Attributes)),
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

    /// <summary>
    /// The id <paramref name="id"/>, an element of <paramref name="type"/>, was read with: without the
    /// prefix the type's <c>natural</c> id rule declares, which the binding does not write.
    /// </summary>
    private static string ReadId(DislSpecification specification, string type, string id) =>
        specification.IdRuleOf(type) is { Strategy: "natural", Prefix: { Length: > 0 } prefix } && id.StartsWith(prefix, StringComparison.Ordinal)
            ? id[prefix.Length..]
            : id;

    /// <summary>
    /// The id <paramref name="id"/> was read with, for an element whose type the change does not say:
    /// without the longest prefix a type's <c>natural</c> id rule declares that it starts with.
    /// </summary>
    private static string ReadId(DislSpecification specification, string id)
    {
        var prefix = specification.Metamodel.Types.Keys.Concat(specification.Metamodel.Relations.Keys)
            .Select(specification.IdRuleOf)
            .Where(rule => rule is { Strategy: "natural", Prefix.Length: > 0 } && id.StartsWith(rule.Prefix, StringComparison.Ordinal))
            .Select(rule => rule.Prefix!)
            .OrderByDescending(declared => declared.Length)
            .FirstOrDefault();
        return prefix is null ? id : id[prefix.Length..];
    }

    private static Dictionary<string, object?> Ends(DislSpecification specification, DislTypeMap map, DislChange.Connect connect)
    {
        var written = Written(specification, map, connect.Type, connect.Attributes);
        written[map.BindingAttributeOf(connect.Type, "source")] = ReadId(specification, connect.SourceId);
        written[map.BindingAttributeOf(connect.Type, "target")] = ReadId(specification, connect.TargetId);
        return written;
    }

    private static Dictionary<string, object?> Written(DislSpecification specification, DislTypeMap map, string type, IReadOnlyDictionary<string, object?> attributes) =>
        attributes.ToDictionary(
            attribute => map.BindingAttributeOf(type, attribute.Key),
            attribute => StoredForm(specification, type, attribute.Key, attribute.Value),
            StringComparer.Ordinal);

    private static object? Stored(DislSpecification specification, string type, object? value) => value switch
    {
        null => null,
        DislElement element => ReadId(specification, element.Type.Name, element.Id),
        IReadOnlyList<object?> list => list.Select(item => Stored(specification, type, item)).ToList(),
        string id when specification.Metamodel.TypeOf(type) is not null => ReadId(specification, type, id),
        long month when type == "yearMonth" => YearMonth.Format(month, "uuuu-MM"),
        string key when specification.Metamodel.Enums.TryGetValue(type, out var enumeration) => enumeration.ValueOf(key)?.Stored ?? key,
        _ => value,
    };
}
