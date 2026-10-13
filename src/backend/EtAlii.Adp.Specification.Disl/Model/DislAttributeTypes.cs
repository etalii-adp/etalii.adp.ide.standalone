using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The attribute types a DISL specification gives the types of a binding, in the shapes
/// <see cref="FblOptions.AttributeType"/> and <see cref="FblOptions.TimeAttributes"/> take, so that
/// FBL 0.5 section 5.2 can read each slot by its type. A binding type is resolved through
/// <c>persistence.typeMap</c>: to the metamodel type it maps to, or to the diagram's attributes, with
/// its attributes renamed; a host attribute, which has no type of its own, is a string, read as text;
/// a type mapped to <c>header</c> or <c>unreadable</c> gives no type.
/// </summary>
public static class DislAttributeTypes
{
    /// <summary>The type of attribute <c>attribute</c> of binding type <c>type</c>, or null where the specification gives none.</summary>
    public static Func<string, string, string?> Of(DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var map = DislTypeMap.Of(specification);
        var metamodel = specification.Metamodel;
        return (type, attribute) =>
        {
            var mapping = map.For(type);
            if (mapping.As is "header" or "unreadable") return null;
            if (mapping.HostAttributes.Contains(attribute)) return "string";
            var name = mapping.Attributes.GetValueOrDefault(attribute, attribute);
            var attributes = mapping.As == "diagram" ? metamodel.DiagramAttributes : metamodel.TypeOf(mapping.As)?.Attributes;
            return attributes is not null && attributes.TryGetValue(name, out var declared) ? declared.Type : null;
        };
    }

    /// <summary>The attributes of <paramref name="binding"/> the specification types as a date or a date-time, keyed <c>rule.attribute</c>.</summary>
    public static IReadOnlySet<string> TimeAttributes(DislSpecification specification, FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var typeOf = Of(specification);
        return binding.AllRules
            .SelectMany(rule => rule.Attributes.Select(attribute => attribute.Key).Where(attribute => typeOf(rule.Type, attribute) is "date" or "datetime").Select(attribute => $"{rule.Name}.{attribute}"))
            .ToHashSet(StringComparer.Ordinal);
    }
}
