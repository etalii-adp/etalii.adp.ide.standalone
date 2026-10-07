using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads with the module's plugin functions.
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the
/// file on disk - so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh timeline &lt;etalii.adp checkout&gt;</c>, never by hand.
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
        var provenance = Json("timeline.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Resource("timeline.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = Json("timeline.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/timeline.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its language id, and its origin equals the catalog origin.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = Json("timeline.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.generic.timeline", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.Timeline.Origin.Key, language.GetProperty("origin").GetString());
    }

    /// <summary>The definition loads, and the runtime has nothing to say about it.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutDiagnostics() => Assert.Empty(TimelineDefinition.Diagnostics);

    /// <summary>
    /// A time is read with the module's own plugin functions, not the definition's fallbacks: those
    /// read ISO 8601 only, where the parser also reads <c>July 5, 2026</c>.
    /// </summary>
    [Theory]
    [InlineData("readableTime('July 5, 2026')", true)]
    [InlineData("readableTime(' 2026-07-05 ')", true)]
    [InlineData("readableTime('2026-02-30')", false)]
    [InlineData("compareTimes('2026-07-05T00:00:00+02:00', '2026-07-04T23:00:00')", -1L)]
    [InlineData("dateAfter('2026-01-05T22:00:00', 6)", "2026-01-11")]
    public void TheModulesPluginFunctions_AreTheOnesCalled(string expression, object expected)
    {
        var value = TimelineDefinition.Specification.Environment(DislContexts.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

        Assert.Equal(expected, value);
    }
}
