using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The four DISL definitions bundled from etalii-adp/etalii.adp are the bytes their provenance says they are, the
/// bytes in <c>definition/</c>, and they load with no diagnostic of severity error; the parts the four repeat are
/// equal in all four (rdf-disl-fbl Requirements 2.2, 2.5).
/// </summary>
/// <remarks>
/// Re-bundle with <c>src/diagrams/tools/bundle-disl.sh rdf &lt;etalii.adp checkout&gt; w3c-rdf w3c-owl w3c-skos w3c-shacl</c>,
/// never by hand.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    /// <summary>The definitions, by file name without its extension, the origin each declares and its block of wire ids.</summary>
    private static readonly (string Name, string Origin, string WireIds)[] _definitions =
    [
        ("w3c-rdf", "w3c/rdf", "x-rdf"),
        ("w3c-owl", "w3c/owl", "x-owl"),
        ("w3c-skos", "w3c/skos", "x-skos"),
        ("w3c-shacl", "w3c/shacl", "x-shacl"),
    ];

    public static TheoryData<string, string> Definitions { get; } = new(_definitions.Select(definition => (definition.Name, definition.Origin)));

    public static TheoryData<string> Names { get; } = new(_definitions.Select(definition => definition.Name));

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_HashesToItsProvenance(string name)
    {
        // Act.
        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes(name + ".dis")));

        // Assert.
        Assert.Equal(_module.ResourceJson(name + ".provenance.json").GetProperty("sha256").GetString(), actual);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheEmbeddedDefinition_IsTheBundledFile(string name)
    {
        // Act.
        var onDisk = await File.ReadAllBytesAsync(BundledFile(name), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(onDisk, _module.ResourceBytes(name + ".dis"));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile(string name)
    {
        // Act.
        var provenance = _module.ResourceJson(name + ".provenance.json");

        // Assert.
        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/" + name + ".dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void TheBundledDefinition_LoadsWithoutAnError_AndIsFoundByItsReadingsOrigin(string name, string origin)
    {
        // Arrange.
        var type = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);

        // Act.
        var definition = RdfDefinitions.For(type.Origin);

        // Assert.
        Assert.Equal(origin, definition.Specification.Origin);
        Assert.Equal("definitions/diagrams/" + name + ".dis", definition.Provenance.Path);
        Assert.DoesNotContain(definition.Diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void EachReading_HasTheWireIdsOfItsOwnBlock(string name, string origin)
    {
        // Arrange.
        var type = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);
        var block = _definitions.Single(definition => definition.Name == name).WireIds;
        var firstTool = _module.ResourceJson(name + ".dis").GetProperty(block).GetProperty("tools").EnumerateObject().First();

        // Act.
        var ids = RdfDefinitions.Ids(type.Origin);

        // Assert.
        Assert.Equal(firstTool.Value.GetProperty("id").GetString(), ids.ToolId(firstTool.Name));
    }

    [Fact]
    public void TheModule_EmbedsTheFourDefinitionsAndNoOther()
    {
        // Act.
        var embedded = _module.GetManifestResourceNames().Where(resource => resource.EndsWith(".dis", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        // Assert.
        Assert.Equal(_definitions.Select(definition => definition.Name + ".dis").Order(StringComparer.Ordinal), embedded);
        Assert.Equal(_definitions.Length, RdfDefinitions.Set.Definitions.Count);
    }

    /// <summary>
    /// The stored types, their id rules and the plugin declaration are equal in all four, compared as parsed data
    /// (Requirement 2.2, its second branch: the four repeat them rather than import them).
    /// </summary>
    [Fact]
    public async Task TheRepeatedParts_AreEqualInAllFour()
    {
        // Arrange.
        var parsed = new List<(string File, JsonNode Definition)>();
        foreach ((string name, string _, string _) in _definitions)
        {
            parsed.Add((name + ".dis", JsonNode.Parse(await File.ReadAllTextAsync(BundledFile(name), TestContext.Current.CancellationToken))!));
        }

        // Act.
        var differences = RepeatedPartDifferences(parsed);

        // Assert.
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    /// <summary>The comparison names the part and the two files, so <see cref="TheRepeatedParts_AreEqualInAllFour"/> cannot pass by comparing nothing.</summary>
    [Fact]
    public async Task TheRepeatedPartComparison_NamesThePartAndTheTwoFiles()
    {
        // Arrange.
        var first = JsonNode.Parse(await File.ReadAllTextAsync(BundledFile("w3c-rdf"), TestContext.Current.CancellationToken))!;
        var changed = first.DeepClone();
        changed["metamodel"]!["types"]!["Prefix"]!.AsObject()["x-changed"] = true;

        // Act.
        var differences = RepeatedPartDifferences([("w3c-rdf.dis", first), ("changed.dis", changed)]);

        // Assert.
        Assert.Equal(["The Prefix type of changed.dis differs from that of w3c-rdf.dis."], differences);
    }

    private static List<string> RepeatedPartDifferences(IReadOnlyList<(string File, JsonNode Definition)> definitions)
    {
        (string firstFile, JsonNode first) = definitions[0];
        var parts = new (string Part, Func<JsonNode, JsonNode?> Of)[]
        {
            ("Triple type", definition => definition["metamodel"]?["types"]?["Triple"]),
            ("Prefix type", definition => definition["metamodel"]?["types"]?["Prefix"]),
            ("Base type", definition => definition["metamodel"]?["types"]?["Base"]),
            ("Triple id rule", definition => definition["persistence"]?["ids"]?["types"]?["Triple"]),
            ("Prefix id rule", definition => definition["persistence"]?["ids"]?["types"]?["Prefix"]),
            ("Base id rule", definition => definition["persistence"]?["ids"]?["types"]?["Base"]),
            ("plugin declaration", definition => definition["plugins"]),
            ("persistence binding", definition => definition["persistence"]?["binding"]),
        };
        return
        [
            .. from definition in definitions.Skip(1)
               from part in parts
               where !JsonNode.DeepEquals(part.Of(first), part.Of(definition.Definition))
               select $"The {part.Part} of {definition.File} differs from that of {firstFile}.",
        ];
    }

    private static string BundledFile(string name) => RepositoryPaths.Under("src", "diagrams", "rdf", "definition", name + ".dis");
}
