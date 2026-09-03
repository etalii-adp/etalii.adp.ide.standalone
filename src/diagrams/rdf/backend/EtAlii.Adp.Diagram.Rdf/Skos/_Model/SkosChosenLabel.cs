namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>What kind of name the chooser ended up with - the canvas styles by this.</summary>
public enum SkosLabelKind
{
    /// <summary>A <c>skos:prefLabel</c> won.</summary>
    Preferred,

    /// <summary>No preferred label existed in any language; an <c>altLabel</c> stands in, marked.</summary>
    Alternate,

    /// <summary>No label at all; the IRI's display form stands in, dimmed - and validation reports it.</summary>
    IriFallback,
}

/// <summary>
/// The one name a node wears (skos-diagram Requirement 3). The language chip is data here, not
/// client logic: the canvas shows a chip exactly when <see cref="LanguageTag"/> is non-empty and
/// differs from the session's display language - an untagged label is language-neutral and wears
/// none.
/// </summary>
/// <param name="Text">The displayed text.</param>
/// <param name="LanguageTag">The chosen label's language tag, lowercased; empty for untagged literals and the IRI fallback.</param>
/// <param name="Kind">What kind of name this is.</param>
public sealed record SkosChosenLabel(string Text, string LanguageTag, SkosLabelKind Kind);
