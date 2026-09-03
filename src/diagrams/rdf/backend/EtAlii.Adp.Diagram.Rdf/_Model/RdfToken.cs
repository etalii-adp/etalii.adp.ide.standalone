namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// One token, addressed by its exact slice of the document text so spans can be recorded to the
/// character.
/// </summary>
/// <param name="Kind">What the token is.</param>
/// <param name="Start">Offset of the token's first character in the document text.</param>
/// <param name="Length">The token's length in characters.</param>
/// <param name="Value">The token's raw text, exactly as written.</param>
public readonly record struct RdfToken(RdfTokenKind Kind, int Start, int Length, string Value)
{
    /// <summary>Offset just past the token's last character.</summary>
    public int End => Start + Length;
}
