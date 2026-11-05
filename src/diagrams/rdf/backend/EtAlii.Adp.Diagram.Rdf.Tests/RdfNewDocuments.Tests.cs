using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Diagram.Rdf.Tests.Parity;
using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The four new documents are the binding's templates (rdf-disl-fbl task 6): the bytes each factory wrote before for
/// every base name the factories fold as <c>{key}</c> does, and a different local name for one that holds a hyphen or
/// starts with a digit, which <c>{key}</c> folds differently (finding B8).
/// </summary>
public class RdfNewDocumentsTests
{
    private static IDiagramDocumentFactory Factory(ToolOrigin origin)
    {
        var services = new ServiceCollection();
        services.AddRdf();
        using var provider = services.BuildServiceProvider();
        return new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(origin)!;
    }

    private static readonly (ToolOrigin Origin, Func<string, string> Today)[] _readings =
    [
        (ServiceCollectionAddRdfExtension.RdfOrigin, TodaysNewDocuments.Rdf),
        (ServiceCollectionAddRdfExtension.OwlOrigin, TodaysNewDocuments.Owl),
        (ServiceCollectionAddSkosExtension.SkosOrigin, TodaysNewDocuments.Skos),
        (ServiceCollectionAddShaclExtension.ShaclOrigin, TodaysNewDocuments.Shacl),
    ];

    public static TheoryData<string, string> SameNames { get; } = new(
        from origin in new[] { "w3c/rdf", "w3c/owl", "w3c/skos", "w3c/shacl" }
        from name in new[] { "My Graph", "people", "Person_Ontology", "weird name?!", "café", "x" }
        select (origin, name));

    [Theory]
    [MemberData(nameof(SameNames))]
    public void ANewDocument_IsTheBytesTheFactoryWroteBefore(string origin, string baseName)
    {
        // Arrange.
        (ToolOrigin reading, Func<string, string> today) = _readings.Single(candidate => candidate.Origin.Key == origin);

        // Act.
        var text = Factory(reading).CreateEmptyDocument(baseName);

        // Assert.
        Assert.Equal(today(baseName), text);
    }

    [Theory]
    [InlineData("my-graph", "ex:my_graph a ex:Resource", "ex:my-graph a ex:Resource")]
    [InlineData("2026 plan", "ex:_2026_plan a ex:Resource", "ex:2026_plan a ex:Resource")]
    public void AHyphenOrALeadingDigit_IsFoldedAsTheKeyFoldsIt(string baseName, string now, string before)
    {
        // Act.
        var text = Factory(ServiceCollectionAddRdfExtension.RdfOrigin).CreateEmptyDocument(baseName);

        // Assert: the one difference from the factory, which the pull request names and task 18 reports.
        Assert.Contains(now, text, StringComparison.Ordinal);
        Assert.Contains(before, TodaysNewDocuments.Rdf(baseName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("w3c/rdf")]
    [InlineData("w3c/owl")]
    [InlineData("w3c/skos")]
    [InlineData("w3c/shacl")]
    public void ANewDocument_ParsesWithoutAFinding(string origin)
    {
        // Arrange.
        var reading = _readings.Single(candidate => candidate.Origin.Key == origin).Origin;

        // Act.
        var text = Factory(reading).CreateEmptyDocument("Starter");
        var model = RdfParser.Parse(LineDocument.Parse(text));

        // Assert.
        Assert.NotEmpty(model.Triples);
        Assert.Empty(RdfValidator.Judge(model));
    }
}
