namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// One target declaration, drawn on its shape's card as a chip - never an edge, never a
/// fabricated node: the data end of the declaration is usually not in this file, and that is the
/// medium working (shacl-diagram Requirements 1.3, 4.3).
/// </summary>
/// <param name="Kind">What the declaration selects by.</param>
/// <param name="TermDisplay">The targeted term's display form - or the literal's lexical form for a literal <c>sh:targetNode</c>.</param>
/// <param name="TermIri">The targeted term's full IRI; empty for a literal target.</param>
/// <param name="DescribedInFile">Whether the targeted term occurs as a subject in this file - a fact the grid states, never a finding.</param>
/// <param name="ShapeIri">The owning shape's IRI - the first third of the chip's address.</param>
/// <param name="PredicateIri">The target predicate - the second third of the address; empty for the implicit chip, which no triple states and no menu removes.</param>
public sealed record ShaclTargetChip(
    ShaclTargetKind Kind,
    string TermDisplay,
    string TermIri,
    bool DescribedInFile,
    string ShapeIri,
    string PredicateIri);
