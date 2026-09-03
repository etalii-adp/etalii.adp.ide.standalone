namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Reads a dialog's term input the way an author writes terms: a full IRI, a bracketed IRI, or
/// a prefixed name under the document's own declarations - and refuses an undeclared prefix by
/// name rather than inventing a declaration (Requirement 5.7).
/// </summary>
internal static class RdfTermInput
{
    /// <summary>
    /// The full IRI <paramref name="value"/> means under <paramref name="model"/>'s
    /// declarations, or null with the refusal saying why not.
    /// </summary>
    public static (string? Iri, string Error) Resolve(RdfModel model, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return (null, "A term needs a name: a full IRI, or a prefixed name like ex:thing.");
        }

        if (trimmed.Contains(' '))
        {
            return (null, "An IRI cannot contain spaces.");
        }

        // Brackets say "this is the IRI itself" - the way out for scheme-only IRIs like
        // urn:..., which are otherwise indistinguishable from a prefixed name.
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
        {
            var inner = trimmed[1..^1].Trim();
            return inner.Length > 0 ? (inner, "") : (null, "The brackets are empty.");
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return (trimmed, "");
        }

        var colon = trimmed.IndexOf(':');
        if (colon < 0)
        {
            return (null, $"'{trimmed}' is neither a full IRI nor a prefixed name. Write ex:{trimmed}, or a full IRI.");
        }

        // Unbracketed with a colon reads as a prefixed name: ex:thing is what an author means
        // far more often than a scheme called ex.
        var prefix = trimmed[..colon];
        var expansion = model.Expansion(prefix);
        return expansion is null
            ? (null, $"The prefix '{prefix}:' is not declared in this file. Declare it first, write the full IRI, or bracket a scheme IRI as <{trimmed}>.")
            : (expansion + trimmed[(colon + 1)..], "");
    }
}
