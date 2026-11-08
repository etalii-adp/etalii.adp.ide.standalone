using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The measurement of Requirement 4.5 (rdf-disl-fbl task 8, finding A10): the two largest examples loaded under each of
/// the four definitions, every derived type's <c>from</c> evaluated over the file's stored elements and charged against
/// the definition's <c>limits.celCost</c>. A row is a document and a reading; its value is the derived types whose items
/// cannot be computed within the limit, which is what <see cref="DislModelBuilder"/> reports as <c>std.derivedFailed</c>
/// and draws none of. Tasks 9 to 11 read their own reading's rows before they switch.
/// </summary>
/// <remarks>
/// <para>
/// The answer is that the RDF graph, OWL and SKOS readings do not fit, so each keeps its projection as the stand-in for
/// gap 6, a grouping function in DISL; the SHACL reading fits on both. The cost, with no limit, measured on 2026-11-08 (the limit is 1,000,000 for all four):
/// </para>
/// <list type="bullet">
/// <item><c>laureates.ttl</c>, 6,596 triples: RDF <c>Statement</c> 14.0 million (14 times the limit); OWL <c>Class</c> 351
/// million, <c>Thing</c> 98 million, <c>Datatype</c> 208 million, <c>Individual</c> 464 million, <c>Assertion</c> 1,454 million.
/// SKOS and SHACL fit, at 0.3 times the limit at most.</item>
/// <item><c>business-economics.ttl</c>, 17,683 triples: RDF <c>Resource</c> and <c>BlankNode</c> 14.9 million each,
/// <c>Statement</c> 243 million; OWL <c>Class</c> 1,182 million, <c>Thing</c> 690 million, <c>Datatype</c> 904 million,
/// <c>Individual</c> 1,843 million and <c>Assertion</c> more; SKOS <c>ConceptScheme</c>, <c>Concept</c>, <c>Collection</c>,
/// <c>Broader</c> and <c>Related</c> over it as well. SHACL fits.</item>
/// </list>
/// <para>
/// The time, against today's: building the RDF graph's model takes 41 s for <c>laureates.ttl</c> and 192 s for
/// <c>business-economics.ttl</c>, where today's parse and projection take 1.0 s and 12.4 s. Most of it is not a derived
/// type at all but the stored triples' own id rule, whose <c>repeatSuffix</c> filters every triple once per triple: each
/// evaluation stays within the limit, which is per evaluation, and the whole is quadratic. Neither number is asserted,
/// since a machine's speed is not a fact about the definition.
/// </para>
/// <para>
/// A derived type that reads the derived nodes of the types before it sees none here, because only the stored elements
/// are in the diagram, so its cost is a lower bound: a type recorded as over the limit is over it, and one recorded as
/// within it may not be once those nodes exist.
/// </para>
/// </remarks>
public class RdfDerivedLoadTests
{
    private const string Laureates = "diagrams/rdf/examples/nobel/laureates.ttl";
    private const string BusinessEconomics = "diagrams/rdf/examples/stw/business-economics.ttl";

    public static TheoryData<string, string, string[]> Rows() => new()
    {
        { Laureates, "rdf", ["Statement"] },
        { Laureates, "owl", ["Class", "Thing", "Datatype", "Individual", "Assertion"] },
        { Laureates, "skos", [] },
        { Laureates, "shacl", [] },
        { BusinessEconomics, "rdf", ["Resource", "BlankNode", "Statement"] },
        { BusinessEconomics, "owl", ["Class", "Thing", "Datatype", "Individual", "Assertion"] },
        { BusinessEconomics, "skos", ["ConceptScheme", "Concept", "Collection", "Broader", "Related"] },
        { BusinessEconomics, "shacl", [] },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void TheDerivedTypesWhoseItemsExceedTheCostLimit_AreTheRecordedOnes(string relative, string reading, string[] exceeding)
    {
        // Arrange: the file's stored elements, as the plugin reads them, in a diagram of the reading's definition.
        var origin = ToolOrigin.TryParse("w3c/" + reading)!;
        var specification = RdfDefinitions.For(origin).Specification;
        var diagram = StoredElements(specification, RdfBody.Open(File.ReadAllBytes(Path.Combine(RdfCorpus.SourceFolder, relative)), relative));
        var environment = specification.Environment(DislContext.Derive);
        var variables = new Dictionary<string, object?> { ["diagram"] = diagram, ["env"] = new CelMap() };

        // Act: each derived type's items, charged against the definition's limit.
        var over = DerivedFroms(reading)
            .Where(derived => environment.Compile(derived.From).Evaluate(variables, new CelBudget(specification.CostLimit)) is CelError { Message: var message }
                && message.Contains("budget", StringComparison.Ordinal))
            .Select(derived => derived.Type)
            .ToList();

        // Assert.
        Assert.Equal(1_000_000, specification.CostLimit);
        Assert.Equal(exceeding, over);
    }

    /// <summary>A diagram of <paramref name="specification"/> holding the stored elements of <paramref name="body"/>, each under an id of its own.</summary>
    private static DislDiagram StoredElements(DislSpecification specification, RdfBody body)
    {
        var diagram = new DislDiagram(specification);
        var elements = body.Reading.Elements;
        for (var index = 0; index < elements.Count; index++)
        {
            diagram.AddNode(elements[index].Type, $"stored:{index}", elements[index].Attributes);
        }

        return diagram;
    }

    /// <summary>The <c>from</c> of every derived node and relation type of the reading's bundled definition, in the order the metamodel declares them.</summary>
    private static IReadOnlyList<(string Type, string From)> DerivedFroms(string reading)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RdfCorpus.SourceFolder, "diagrams", "rdf", "definition", $"w3c-{reading}.dis")));
        var metamodel = json.RootElement.GetProperty("metamodel");
        return
        [
            .. ((string[])["types", "relations"])
                .SelectMany(kind => metamodel.GetProperty(kind).EnumerateObject())
                .Where(type => type.Value.TryGetProperty("derived", out var derived) && derived.ValueKind == JsonValueKind.Object && derived.TryGetProperty("from", out _))
                .Select(type => (type.Name, type.Value.GetProperty("derived").GetProperty("from").GetString()!)),
        ];
    }
}
