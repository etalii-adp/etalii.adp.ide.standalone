using System.Text;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The minimal document a new ontology diagram starts as: an <c>owl:Ontology</c> header - the
/// marker the routing arrangement suggests this reading off - and one labeled class, parsing
/// and validating clean under both the family's and this reading's rules.
/// </summary>
/// <remarks>
/// CRLF and a trailing newline, like the family's own factory: a file ADP creates has no
/// existing style to preserve, and CRLF is the repository's house style.
/// </remarks>
public sealed class OwlDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalOf(baseName);
        return $"@prefix : <http://example.org/{local}#> .\r\n"
            + "@prefix owl: <http://www.w3.org/2002/07/owl#> .\r\n"
            + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
            + "\r\n"
            + $"<http://example.org/{local}> a owl:Ontology ;\r\n"
            + $"    rdfs:label \"{baseName}\" .\r\n"
            + "\r\n"
            + ":Thing1 a owl:Class ;\r\n"
            + "    rdfs:label \"First class\" .\r\n";
    }

    /// <summary>The base name as a local name, anything a prefixed name cannot carry folded to underscores.</summary>
    private static string LocalOf(string baseName)
    {
        var builder = new StringBuilder(baseName.Length);
        foreach (var character in baseName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_');
        }

        var local = builder.ToString().Trim('_');
        return local.Length > 0 ? local : "untitled";
    }
}
