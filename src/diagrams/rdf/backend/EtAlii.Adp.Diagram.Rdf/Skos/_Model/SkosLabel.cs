namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Which SKOS labeling property stated a label.</summary>
public enum SkosLabelSource
{
    Preferred,
    Alternate,
    Hidden,
}

/// <summary>
/// One label literal of one concept, scheme or collection, with the triple that stated it - the
/// property grid lists them all, the chooser picks one, and an edit splices exactly this triple.
/// </summary>
/// <param name="Source">Which labeling property stated it.</param>
/// <param name="Text">The literal's lexical form.</param>
/// <param name="Language">The language tag, lowercased for comparison per BCP 47's case rule; null for an untagged literal.</param>
/// <param name="Triple">The stating triple - the writer's coordinates, valid only for the parse they came from.</param>
public sealed record SkosLabel(SkosLabelSource Source, string Text, string? Language, RdfTriple Triple);
