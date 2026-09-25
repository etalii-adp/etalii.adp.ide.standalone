using System.Text;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The minimal document a new shapes diagram starts as: one node shape with a class target and
/// one property row, parsing and validating clean, so a fresh shapes file opens as a card with
/// something on it rather than as an empty canvas.
/// </summary>
/// <remarks>
/// The starter targets a class the file does not describe, which is the normal case for this
/// medium and not a finding - a shapes graph aims at data held elsewhere (Requirement 4.3). The
/// chip on the new card says so, which makes the first thing a user sees also the thing this
/// reading most needs them to understand.
/// </remarks>
public sealed class ShaclDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalOf(baseName);
        return "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n"
            + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\r\n"
            + "@prefix ex: <http://example.org/> .\r\n"
            + "\r\n"
            + $"ex:{local}Shape a sh:NodeShape ;\r\n"
            + $"    sh:targetClass ex:{local} ;\r\n"
            + $"    sh:name \"{baseName}\" ;\r\n"
            + "    sh:property [\r\n"
            + "        sh:path ex:name ;\r\n"
            + "        sh:datatype xsd:string ;\r\n"
            + "        sh:minCount 1 ;\r\n"
            + "        sh:maxCount 1 ;\r\n"
            + "    ] .\r\n";
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
