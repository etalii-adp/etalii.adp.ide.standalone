using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// the bytes in <c>definition/</c>, and it loads with no diagnostic (causal-loop-disl-fbl
/// Requirements 2.4 and 2.5).
/// </summary>
/// <remarks>
/// Nothing derives from the definition yet. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh causal-loop-diagram &lt;etalii.adp checkout&gt;</c>, never by hand.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private const string Name = "causal-loop-diagram";

    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in provenance.json is the sha256 of the embedded <c>.dis</c> bytes.</summary>
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
        var file = Path.Combine(RepositoryPaths.Under("src", "diagrams", Name), "definition", Name + ".dis");

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

    /// <summary>The bundled definition is this module's: its origin equals the catalog origin.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = _module.ResourceJson(Name + ".dis").GetProperty("language");

        Assert.Equal(Diagram.CausalLoop.Origin.Key, language.GetProperty("origin").GetString());
    }

    /// <summary>The definition loads, and the runtime has nothing to say about it.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutDiagnostics() =>
        Assert.Empty(BundledDefinition.Load(_module, Name + ".dis").Diagnostics);
}
