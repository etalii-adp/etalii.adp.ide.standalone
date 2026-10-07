using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is.
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the
/// file on disk - so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh mindmap &lt;etalii.adp checkout&gt;</c>, never by hand.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    private static byte[] Resource(string logicalName)
    {
        using var stream = _module.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"The module embeds no resource {logicalName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static JsonElement Json(string logicalName) => JsonDocument.Parse(Resource(logicalName)).RootElement;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in provenance.json is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        var provenance = Json("mindmap.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Resource("mindmap.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = Json("mindmap.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/mindmap.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its language id, and its origin equals the catalog origin.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = Json("mindmap.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.freeplane.mindmap", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.Mindmap.Origin.Key, language.GetProperty("origin").GetString());
    }

    /// <summary>The bundled definition loads with this runtime without a diagnostic: every expression compiles in its own context.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutADiagnostic()
    {
        // Act.
        var bundled = BundledDefinition.Load(_module, "mindmap.dis");

        // Assert.
        Assert.Empty(bundled.Diagnostics);
        Assert.Equal("0.3", bundled.Specification.Disl);
    }
}
