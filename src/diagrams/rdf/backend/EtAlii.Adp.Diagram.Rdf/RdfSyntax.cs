namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>Which of the two syntaxes a body is written in, as its file's extension says.</summary>
/// <remarks>
/// <b>The stand-in for gap 5</b> (rdf-disl-fbl, finding B7): FBL hands a plugin's <c>read</c> and
/// <c>plan</c> the body's bytes and not its name, so <see cref="RdfBody"/> tells
/// <see cref="RdfTurtlePlugin"/> the syntax when it constructs it. It goes when FBL tells a plugin the
/// body's name.
/// </remarks>
public enum RdfSyntax
{
    /// <summary>Turtle, a <c>.ttl</c> file, and any file whose extension is not <c>.nt</c>.</summary>
    Turtle,

    /// <summary>N-Triples, a <c>.nt</c> file: one full statement per line, a subset of Turtle.</summary>
    NTriples,
}
