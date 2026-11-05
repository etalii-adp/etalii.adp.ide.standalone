using System.Reflection;
using System.Text.Json.Nodes;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The four DISL definitions bundled from etalii-adp/etalii.adp are the bytes their provenance says they are, the
/// bytes in <c>definition/</c>, and they load with no diagnostic of severity error; the parts the four repeat are
/// equal in all four (rdf-disl-fbl Requirements 2.2, 2.5).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionSetContract<BundledDefinitionTests>, IBundledDefinitions
{
    /// <summary>Each definition's block of wire ids.</summary>
    private static readonly Dictionary<string, string> _wireIds = new(StringComparer.Ordinal)
    {
        ["w3c-rdf"] = "x-rdf",
        ["w3c-owl"] = "x-owl",
        ["w3c-skos"] = "x-skos",
        ["w3c-shacl"] = "x-shacl",
    };

    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "rdf";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("w3c-rdf", Diagram.Rdf.Origin.Key, "net.etalii.adp.w3c.rdf"),
        new("w3c-owl", Diagram.Owl.Origin.Key, "net.etalii.adp.w3c.owl"),
        new("w3c-skos", Diagram.Skos.Origin.Key, "net.etalii.adp.w3c.skos"),
        new("w3c-shacl", Diagram.Shacl.Origin.Key, "net.etalii.adp.w3c.shacl"),
    ];

    /// <summary>The definitions and their origins.</summary>
    public static TheoryData<string, string> Origins { get; } = new(Definitions.Select(definition => (definition.Name, definition.Origin)));

    protected override bool AWarningIsAllowed => true;

    /// <summary>The module holds the four as one set.</summary>
    protected override IReadOnlyCollection<BundledDefinition> Held => RdfDefinitions.Set.Definitions;

    /// <summary>
    /// The stored types, their id rules and the plugin declaration (Requirement 2.2, its second branch: the four repeat
    /// them rather than import them).
    /// </summary>
    protected override IReadOnlyList<(string Part, Func<JsonNode, JsonNode?> Of)> RepeatedParts { get; } =
    [
        ("Triple type", definition => definition["metamodel"]?["types"]?["Triple"]),
        ("Prefix type", definition => definition["metamodel"]?["types"]?["Prefix"]),
        ("Base type", definition => definition["metamodel"]?["types"]?["Base"]),
        ("Triple id rule", definition => definition["persistence"]?["ids"]?["types"]?["Triple"]),
        ("Prefix id rule", definition => definition["persistence"]?["ids"]?["types"]?["Prefix"]),
        ("Base id rule", definition => definition["persistence"]?["ids"]?["types"]?["Base"]),
        ("plugin declaration", definition => definition["plugins"]),
        ("persistence binding", definition => definition["persistence"]?["binding"]),
    ];

    /// <summary>A definition as the module holds it: the one found by its reading's origin.</summary>
    protected override BundledDefinition Load(BundledDefinitionExpectation expected) =>
        RdfDefinitions.For(Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == expected.Origin).Origin);

    [Theory]
    [MemberData(nameof(Origins))]
    public void EachReading_HasTheWireIdsOfItsOwnBlock(string name, string origin)
    {
        // Arrange.
        var type = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);
        var block = _wireIds[name];
        var firstTool = Module.ResourceJson(name + ".dis").GetProperty(block).GetProperty("tools").EnumerateObject().First();

        // Act.
        var ids = RdfDefinitions.Ids(type.Origin);

        // Assert.
        Assert.Equal(firstTool.Value.GetProperty("id").GetString(), ids.ToolId(firstTool.Name));
    }
}
