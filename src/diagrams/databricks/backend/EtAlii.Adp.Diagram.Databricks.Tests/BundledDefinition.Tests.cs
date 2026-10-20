using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The three DISL definitions bundled from etalii-adp/etalii.adp, one per Databricks tool type, are the bytes their
/// provenance says they are, the bytes in <c>definition/</c>, and they load with no diagnostic of severity error
/// (databricks-disl-fbl Requirements 2.3 and 2.4).
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the file on disk,
/// so a build that embedded a stale or rewritten copy fails here too. Nothing derives from the definitions yet.
/// Re-bundle with <c>src/diagrams/tools/bundle-disl.sh databricks &lt;etalii.adp checkout&gt; databricks-bundle
/// databricks-job databricks-pipeline</c>, never by hand.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    /// <summary>The definitions, by file name without its extension, and the catalog type each is the definition of.</summary>
    private static readonly (string Name, DiagramDefinition Type)[] _definitions =
    [
        ("databricks-bundle", Diagram.Bundle),
        ("databricks-job", Diagram.Job),
        ("databricks-pipeline", Diagram.Pipeline),
    ];

    /// <summary>The definitions, by file name without its extension.</summary>
    public static TheoryData<string> Names { get; } = new(_definitions.Select(definition => definition.Name));

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
        var onDisk = await File.ReadAllBytesAsync(RepositoryPaths.Under("src", "diagrams", "databricks", "definition", name + ".dis"), TestContext.Current.CancellationToken);

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

    /// <summary>
    /// Each definition loads, through the loader that checks its provenance, with no diagnostic of severity error
    /// (Requirement 2.4), and declares the catalog origin of the tool type it is the definition of.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_LoadsWithoutAnError(string name)
    {
        var type = _definitions.Single(candidate => candidate.Name == name).Type;

        var definition = BundledDefinition.Load(_module, name + ".dis");

        Assert.DoesNotContain(definition.Diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
        Assert.Equal(type.Origin.Key, definition.Specification.Origin);
        Assert.Equal("definitions/diagrams/" + name + ".dis", definition.Provenance.Path);
    }

    /// <summary>The module embeds the three definitions and no other, so a fourth cannot be bundled unnoticed.</summary>
    [Fact]
    public void TheModule_EmbedsTheThreeDefinitionsAndNoOther()
    {
        var embedded = _module.GetManifestResourceNames().Where(resource => resource.EndsWith(".dis", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        Assert.Equal(_definitions.Select(definition => definition.Name + ".dis").Order(StringComparer.Ordinal), embedded);
    }
}
