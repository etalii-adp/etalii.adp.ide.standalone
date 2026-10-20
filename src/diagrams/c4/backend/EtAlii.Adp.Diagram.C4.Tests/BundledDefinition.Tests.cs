using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The six DISL definitions bundled from etalii-adp/etalii.adp are the bytes their provenance says they
/// are, the bytes in <c>definition/</c>, and they load with no diagnostic of severity error; the layers
/// they share are equal; and <c>c4/code</c> has none (c4-disl-fbl Requirements 2.3, 2.5 to 2.7).
/// </summary>
/// <remarks>
/// Nothing derives from the definitions yet. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh c4 &lt;etalii.adp checkout&gt; c4-system-landscape c4-context
/// c4-container c4-component c4-dynamic c4-deployment</c>, never by hand.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    /// <summary>The definitions, by file name without its extension, and the origin each declares.</summary>
    private static readonly (string Name, string Origin)[] _definitions =
    [
        ("c4-system-landscape", "c4/system-landscape"),
        ("c4-context", "c4/context"),
        ("c4-container", "c4/container"),
        ("c4-component", "c4/component"),
        ("c4-dynamic", "c4/dynamic"),
        ("c4-deployment", "c4/deployment"),
    ];

    /// <summary>The definitions and their origins.</summary>
    public static TheoryData<string, string> Definitions { get; } = new(_definitions);

    /// <summary>The definitions, by file name without its extension.</summary>
    public static TheoryData<string> Names { get; } = new(_definitions.Select(definition => definition.Name));

    /// <summary>The layers the six share, which Requirement 2.3 holds equal in all six.</summary>
    private static readonly string[] _sharedLayers = ["metamodel", "notation", "forms", "constraints", "behavior", "persistence"];

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in the provenance is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_HashesToItsProvenance(string name)
    {
        var provenance = _module.ResourceJson(name + ".provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes(name + ".dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of the file in <c>definition/</c>, so a stale build cannot pass.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheEmbeddedDefinition_IsTheBundledFile(string name)
    {
        var onDisk = await File.ReadAllBytesAsync(BundledFile(name), TestContext.Current.CancellationToken);

        Assert.Equal(onDisk, _module.ResourceBytes(name + ".dis"));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile(string name)
    {
        var provenance = _module.ResourceJson(name + ".provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/" + name + ".dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>Each definition declares the catalog origin of the type it is found by, and is the one found by that type's view kind.</summary>
    [Theory]
    [MemberData(nameof(Definitions))]
    public void TheBundledDefinition_IsFoundByItsTypesOriginAndViewKind(string name, string origin)
    {
        var type = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);
        (ToolOrigin _, C4ViewKind viewKind, string resource) = Assert.Single(C4Definitions.Types, candidate => candidate.Origin == type.Origin);

        Assert.Equal(name + ".dis", resource);
        Assert.Equal(origin, C4Definitions.For(type.Origin).Specification.Origin);
        Assert.Same(C4Definitions.For(type.Origin), C4Definitions.For(viewKind));
        Assert.Equal("definitions/diagrams/" + name + ".dis", C4Definitions.For(type.Origin).Provenance.Path);
    }

    /// <summary>
    /// Each definition loads with no diagnostic of severity error (Requirement 2.6). The definitions are
    /// DISL 0.5, of which the runtime warns; a warning is allowed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_LoadsWithoutAnError(string name)
    {
        var path = "definitions/diagrams/" + name + ".dis";

        var definition = Assert.Single(C4Definitions.Set.Definitions, candidate => candidate.Provenance.Path == path);

        Assert.DoesNotContain(definition.Diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
        Assert.DoesNotContain(C4Definitions.Diagnostics, entry => entry.Path == path && entry.Diagnostic.Severity == LoadSeverity.Error);
    }

    /// <summary>The module embeds the six definitions and no other, so a seventh cannot be bundled unnoticed.</summary>
    [Fact]
    public void TheModule_EmbedsTheSixDefinitionsAndNoOther()
    {
        var embedded = _module.GetManifestResourceNames().Where(resource => resource.EndsWith(".dis", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        Assert.Equal(_definitions.Select(definition => definition.Name + ".dis").Order(StringComparer.Ordinal), embedded);
        Assert.Equal(_definitions.Length, C4Definitions.Set.Definitions.Count);
    }

    /// <summary>
    /// The layers the six share are equal in all six, compared as parsed data (Requirement 2.3, Q8), so
    /// a change made to one and not the others is caught here rather than as a view that behaves apart.
    /// </summary>
    [Fact]
    public async Task TheSharedLayers_AreEqualInAllSix()
    {
        var parsed = new List<(string File, JsonNode Definition)>();
        foreach ((string name, string _) in _definitions)
        {
            var text = await File.ReadAllTextAsync(BundledFile(name), TestContext.Current.CancellationToken);
            parsed.Add((name + ".dis", JsonNode.Parse(text)!));
        }

        // The sentences in full, because a collection's failure message cuts them short of the second file.
        var differences = SharedLayerDifferences(parsed);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    /// <summary>
    /// The comparison names the layer and the two files when one definition's shared layer differs, so
    /// <see cref="TheSharedLayers_AreEqualInAllSix"/> cannot pass by comparing nothing.
    /// </summary>
    [Fact]
    public async Task TheSharedLayerComparison_NamesTheLayerAndTheTwoFiles()
    {
        var first = JsonNode.Parse(await File.ReadAllTextAsync(BundledFile("c4-context"), TestContext.Current.CancellationToken))!;
        var changed = first.DeepClone();
        changed["forms"]!.AsObject()["x-changed"] = true;

        var differences = SharedLayerDifferences([("c4-context.dis", first), ("changed.dis", changed)]);

        Assert.Equal(["The forms layer of changed.dis differs from that of c4-context.dis."], differences);
    }

    /// <summary>
    /// <c>c4/code</c> keeps its registration and its refusing empty document, and has no definition
    /// (Requirement 2.7).
    /// </summary>
    [Fact]
    public void TheCodeType_HasNoDefinitionAndStillRefuses()
    {
        var services = new ServiceCollection();
        services.AddC4();
        using var provider = services.BuildServiceProvider();
        var factory = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(Diagram.Code.Origin);

        Assert.Null(C4Definitions.Find(Diagram.Code.Origin));
        Assert.Throws<KeyNotFoundException>(() => C4Definitions.For(Diagram.Code.Origin));
        Assert.DoesNotContain(C4Definitions.Types, type => type.Origin == Diagram.Code.Origin);
        Assert.NotNull(factory);
        var exception = Assert.Throws<NotSupportedException>(() => factory.CreateEmptyDocument("anything"));
        Assert.Equal(ServiceCollectionAddC4Extension.CodeUnavailable, exception.Message);
    }

    /// <summary>Every shared layer of each definition after the first that is not equal to the first's, as a sentence naming the layer and the two files.</summary>
    private static List<string> SharedLayerDifferences(IReadOnlyList<(string File, JsonNode Definition)> definitions)
    {
        (string firstFile, JsonNode first) = definitions[0];
        return
        [
            .. from definition in definitions.Skip(1)
               from layer in _sharedLayers
               where !JsonNode.DeepEquals(first[layer], definition.Definition[layer])
               select $"The {layer} layer of {definition.File} differs from that of {firstFile}.",
        ];
    }

    private static string BundledFile(string name) => RepositoryPaths.Under("src", "diagrams", "c4", "definition", name + ".dis");
}
