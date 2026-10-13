using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Rules;

/// <summary>What a slot of a yaml or json body reads from a scalar (FBL 0.5 §5.2).</summary>
internal enum ReadMode
{
    /// <summary>Neither the binding nor the caller says: the value as the family types it, as before FBL 0.5.</summary>
    Legacy,

    /// <summary>The written text (FBL 0.5 §4.1.5).</summary>
    Text,

    /// <summary>The typed value, converted to the attribute's type.</summary>
    Typed,
}

internal static class ReadModes
{
    /// <summary>
    /// The binding's <c>read</c> first; else text for an attribute the caller types as a string, or
    /// for a reference; else typed when the caller gives a type; else <see cref="ReadMode.Legacy"/>.
    /// </summary>
    private static ReadMode Of(AttributeBinding binding, string? type) => binding.Read switch
    {
        "text" => ReadMode.Text,
        "typed" => ReadMode.Typed,
        _ when type == "string" || binding.Reference is not null => ReadMode.Text,
        _ when type is not null => ReadMode.Typed,
        _ => ReadMode.Legacy,
    };

    /// <summary>The mode of an attribute of <paramref name="rule"/>, with the type the caller gives it.</summary>
    public static ReadMode Of(Rule rule, string attribute, AttributeBinding binding, FblOptions options) =>
        Of(binding, options.AttributeType?.Invoke(rule.Type, attribute));
}
