using System.Text;
using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The minimal document a new RDF diagram starts as - one prefix, one typed resource with a
/// label, parsing and validating clean, because a skeleton that opens with findings would be a
/// refusal factory.
/// </summary>
/// <remarks>
/// CRLF and a trailing newline: the writers preserve whatever a file already uses, but a file
/// ADP creates has no existing style to preserve, and CRLF is the repository's own house style.
/// </remarks>
public sealed class RdfDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalOf(baseName);
        return "@prefix ex: <http://example.org/> .\r\n"
            + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
            + "\r\n"
            + $"ex:{local} a ex:Resource ;\r\n"
            + $"    rdfs:label \"{baseName}\" .\r\n";
    }

    /// <summary>
    /// The base name as a local name: anything a prefixed name cannot carry folded to
    /// underscores, so "My Graph.data" starts life as <c>My_Graph_data</c>.
    /// </summary>
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
