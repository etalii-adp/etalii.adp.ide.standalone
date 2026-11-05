using EtAlii.Adp.Specification.Fbl;
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
/// <b>A <c>fixed</c> attribute is never written</b> (§4.3): a change that names one writes the others.
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
        var map = specification.TypeMap;
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

    /// <summary>The stored form of <paramref name="value"/> for an attribute of type <paramref name="type"/>.</summary>
    public static object? StoredOfType(DislSpecification specification, string type, object? value)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Stored(specification, type, value);
    }

    /// <summary>
    /// The FBL changes that set diagram attribute <paramref name="attribute"/> to <paramref name="value"/> (null to forget it)
    /// against <paramref name="reading"/> (etalii.adp spec 074). A diagram attribute lives in an entry of a binding type
    /// <c>persistence.typeMap</c> maps as <c>diagram</c> and whose <c>attributes</c> name it.
    /// </summary>
    /// <remarks>
    /// A value goes to the last such entry that states the attribute, else the last such entry, else a new one, which stands
    /// right after the entries at the top of the body mapped as the header or as an earlier type of the diagram, or where
    /// its rule's insert says when there are none. A forgotten attribute leaves every entry that states it, last first so
    /// that no earlier id moves, and an entry left with nothing to state is removed with it.
    /// </remarks>
    public static IReadOnlyList<ModelChange> DiagramSet(DislSpecification specification, FblModel reading, string attribute, object? value)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(reading);
        var map = specification.TypeMap;
        if (map.DiagramTypeHolding(attribute) is not { } type) throw new ArgumentException($"This file has no place for the diagram's {attribute}.", nameof(attribute));
        var mapping = map.For(type);
        var name = mapping.Attributes.First(rename => rename.Value == attribute).Key;
        var entries = reading.Elements.Where(element => element.Type == type).ToList();
        if (value is null)
        {
            return [.. entries.Where(entry => States(entry, name)).Reverse().Select(entry => Forgotten(mapping, entry, name))];
        }

        var written = new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = value };
        if ((entries.LastOrDefault(entry => States(entry, name)) ?? entries.LastOrDefault()) is { } target) return [new ModelChange.Set(target.Id, written)];
        var before = map.DiagramTypesBefore(type);
        var anchor = reading.Elements.TakeWhile(element => map.For(element.Type).As == "header" || before.Contains(element.Type)).LastOrDefault();
        return [anchor is null ? new ModelChange.Add(type, null, written) : new ModelChange.AddAfter(type, written, anchor.Id)];
    }

    private static bool States(FblElement entry, string name) => entry.Attributes.TryGetValue(name, out var stated) && stated is not null;

    private static ModelChange Forgotten(DislTypeMapping mapping, FblElement entry, string name) =>
        mapping.Attributes.Keys.Any(other => other != name && States(entry, other))
            ? new ModelChange.Set(entry.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = null })
            : new ModelChange.Remove(entry.Id);

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
        attributes.Where(attribute => !IsFixed(specification, type, attribute.Key)).ToDictionary(
            attribute => map.BindingAttributeOf(type, attribute.Key),
            attribute => StoredForm(specification, type, attribute.Key, attribute.Value),
            StringComparer.Ordinal);

    /// <summary>Whether <paramref name="attribute"/> of <paramref name="type"/> is <c>fixed</c> (§4.3), which a writer never writes.</summary>
    private static bool IsFixed(DislSpecification specification, string type, string attribute) =>
        specification.Metamodel.TypeOf(type) is { } declared && declared.Attributes.TryGetValue(attribute, out var definition) && definition.IsFixed;

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
