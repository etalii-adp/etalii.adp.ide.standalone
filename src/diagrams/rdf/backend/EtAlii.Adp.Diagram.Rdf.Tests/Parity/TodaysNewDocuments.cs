using System.Text;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The four new documents as the module wrote them before rdf-disl-fbl task 6 made them the binding's templates, kept
/// as the oracle the templates are compared with: byte for byte for every base name but one that holds a hyphen or
/// starts with a digit, which <c>{key}</c> folds differently.
/// </summary>
internal static class TodaysNewDocuments
{
    public static string Rdf(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalName.Of(baseName);
        return "@prefix ex: <http://example.org/> .\r\n"
            + "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\r\n"
            + "\r\n"
            + $"ex:{local} a ex:Resource ;\r\n"
            + $"    rdfs:label \"{baseName}\" .\r\n";
    }

    public static string Owl(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalName.Of(baseName);
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

    public static string Skos(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalName.Of(baseName);
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

    public static string Shacl(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var local = LocalName.Of(baseName);
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
}

/// <summary>The factories' own folding of a base name into a local name, which <c>{key}</c> does not reproduce.</summary>
internal static class LocalName
{
    /// <summary>
    /// <paramref name="baseName"/> with anything a prefixed name cannot carry folded to
    /// underscores, so "My Graph.data" becomes <c>My_Graph_data</c>; "untitled" when nothing is left.
    /// </summary>
    public static string Of(string baseName)
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
