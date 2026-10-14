using System.Globalization;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The <c>natural</c> id strategy (DISL §11.5, §11.5.1) as a binding's reader asks for it
/// (<see cref="FblOptions.DeriveId"/>): the id an entry whose rule stores none is read with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The natural id is the string forms of the type's <c>key</c> attributes</b>, in declaration order
/// after inheritance is flattened, joined by <c>|</c>. A binding type is resolved through
/// <c>persistence.typeMap</c> to the metamodel type it maps to, and each key attribute is found under
/// the binding's name for it.
/// </para>
/// <para>
/// <b>Without the prefix</b>: <see cref="DislModelBuilder"/> puts the type's <c>prefix</c> before the id
/// an element was read with, so the reading carries the natural id alone, and a reference by that id
/// resolves under the prefix as every other does.
/// </para>
/// <para>
/// <b>No id is derived</b> (the reader addresses the entry by its place) for a type the metamodel does
/// not have, under another strategy, without a key attribute, or with a key attribute the entry holds
/// no text for.
/// </para>
/// </remarks>
public static class DislNaturalIds
{
    /// <summary>The derivation for <paramref name="specification"/>, in the shape <see cref="FblOptions.DeriveId"/> takes.</summary>
    public static Func<IdRequest, string?> Of(DislSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var map = DislTypeMap.Of(specification);
        return request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var mapping = map.For(request.Type);
            if (specification.Metamodel.TypeOf(mapping.As) is not { } type || specification.IdRuleOf(type.Name).Strategy != "natural") return null;

            var keys = type.Attributes.Values.Where(attribute => DislJson.Bool(attribute.Json, "key")).ToList();
            if (keys.Count == 0) return null;

            var parts = new List<string>(keys.Count);
            foreach (var key in keys)
            {
                var name = mapping.Attributes.FirstOrDefault(rename => rename.Value == key.Name).Key ?? key.Name;
                if (Text(request.Attributes.GetValueOrDefault(name)) is not { Length: > 0 } text) return null;
                parts.Add(text);
            }
            return string.Join('|', parts);
        };
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable => null,
        _ => value.ToString(),
    };
}
