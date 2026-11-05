using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>The minimal new document: parses, validates clean, and carries the base name.</summary>
public class RdfEmptyDocumentTests
{
    [Fact]
    public void ANewDocument_ParsesAndValidatesClean()
    {
        // Act.
        var text = ServiceCollectionAddRdfExtension.EmptyRdfDocument("My Graph");
        var model = RdfParser.Parse(LineDocument.Parse(text));

        // Assert.
        // A skeleton that opened with findings would be a refusal factory.
        Assert.NotEmpty(model.Triples);
        Assert.Empty(RdfValidator.Judge(model));
        Assert.Contains(model.Triples, t => t.Object is LiteralTerm { Lexical: "My Graph" });
    }

    [Fact]
    public void AHostileBaseName_FoldsToACleanLocalName()
    {
        // Act: a base name is a file's, so it holds no path separator.
        var text = ServiceCollectionAddRdfExtension.EmptyRdfDocument("weird name?!");

        // Assert.
        var model = RdfParser.Parse(LineDocument.Parse(text));
        Assert.Contains(model.Triples, t => t.Subject is IriTerm { Iri: "http://example.org/weird_name" });
    }
}
