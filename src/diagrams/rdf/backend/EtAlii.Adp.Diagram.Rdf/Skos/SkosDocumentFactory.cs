using System.Text;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The minimal document a new scheme diagram starts as - one scheme, one top concept with a
/// preferred label, parsing and validating clean (nothing above info), so a fresh vocabulary
/// opens as a vocabulary rather than a refusal.
/// </summary>
public sealed class SkosDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalOf(baseName);
        return "@prefix skos: <http://www.w3.org/2004/02/skos/core#> .\r\n"
            + "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + $"ex:{local} a skos:ConceptScheme ;\r\n"
            + $"    skos:prefLabel \"{baseName}\"@en ;\r\n"
            + $"    skos:hasTopConcept ex:{local}_top .\r\n"
            + "\r\n"
            + $"ex:{local}_top a skos:Concept ;\r\n"
            + "    skos:prefLabel \"Top concept\"@en ;\r\n"
            + $"    skos:topConceptOf ex:{local} .\r\n";
    }

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
