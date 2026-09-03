namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>A blank node.</summary>
/// <remarks>
/// The ordinal is assigned in document order of first appearance and is deterministic for a given
/// document text - the same file always parses to the same ordinals. It is honest only within one
/// parse: nothing ties ordinal 3 of this parse to ordinal 3 of the next, which is exactly the
/// blank-node identity boundary - blank nodes draw, but nothing that must survive a reparse may
/// key off them.
/// </remarks>
/// <param name="Label">The written label of a <c>_:name</c> node, or null for an anonymous <c>[]</c> or collection node.</param>
/// <param name="Ordinal">Position in document order of first appearance, zero-based, unique within one parse.</param>
public sealed record BlankTerm(string? Label, int Ordinal) : RdfTerm;
