using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// the bytes in <c>definition/</c>, and it loads with no diagnostic, so none of severity error
/// (sparql-disl-fbl Requirements 2.4 and 2.5).
/// </summary>
/// <remarks>
/// Nothing derives from the definition yet. Its name is not the module folder's, so it is bundled by
/// name: <c>src/diagrams/tools/bundle-disl.sh sparql &lt;etalii.adp checkout&gt; sparql-query</c>, never by hand,
/// and its provenance is <c>sparql-query.provenance.json</c>.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private const string Name = "sparql-query";

    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in the provenance is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        var provenance = _module.ResourceJson(Name + ".provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes(Name + ".dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of the file in <c>definition/</c>, so a stale build cannot pass.</summary>
    [Fact]
    public async Task TheEmbeddedDefinition_IsTheBundledFile()
    {
        var file = RepositoryPaths.Under("src", "diagrams", "sparql", "definition", Name + ".dis");

        var onDisk = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(onDisk, _module.ResourceBytes(Name + ".dis"));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = _module.ResourceJson(Name + ".provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/" + Name + ".dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its origin, title and icon equal the catalog's.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = _module.ResourceJson(Name + ".dis").GetProperty("language");

        Assert.Equal(Diagram.Sparql.Origin.Key, language.GetProperty("origin").GetString());
        Assert.Equal(Diagram.Sparql.Title, language.GetProperty("label").GetString());
        Assert.Equal(Diagram.Sparql.Icon, language.GetProperty("icon").GetString());
    }

    /// <summary>The definition loads, and the runtime has nothing to say about it: no error, and no warning either.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutDiagnostics() =>
        Assert.Empty(BundledDefinition.Load(_module, Name + ".dis").Diagnostics.Select(diagnostic => diagnostic.ToString()));
}
