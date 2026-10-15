using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads.
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the
/// file on disk - so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh helm-chart &lt;etalii.adp checkout&gt;</c>, never by hand. Nothing
/// derives from the definition yet; the parity transcript is what holds today's behaviour.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in provenance.json is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        var provenance = _module.ResourceJson("helm-chart.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes("helm-chart.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of <c>definition/helm-chart.dis</c>.</summary>
    [Fact]
    public void TheEmbeddedDefinition_EqualsTheDefinitionFolder()
    {
        var onDisk = File.ReadAllBytes(Path.Combine(DefinitionFolder(), "helm-chart.dis"));

        Assert.Equal(onDisk, _module.ResourceBytes("helm-chart.dis"));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = _module.ResourceJson("helm-chart.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/helm-chart.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its origin, title and icon equal the catalog's.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = _module.ResourceJson("helm-chart.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.helm.chart", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.HelmCharts.Origin.Key, language.GetProperty("origin").GetString());
        Assert.Equal(Diagram.HelmCharts.Title, language.GetProperty("label").GetString());
        Assert.Equal(Diagram.HelmCharts.Icon, language.GetProperty("icon").GetString());
    }

    /// <summary>The runtime loads the definition with no diagnostic of severity error.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutAnError()
    {
        var loaded = BundledDefinition.Load(_module, "helm-chart.dis");

        Assert.DoesNotContain(loaded.Diagnostics, d => d.Severity == DislSeverity.Error);
    }

    private static string DefinitionFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "diagrams", "helm-chart", "definition");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/diagrams/helm-chart/definition was not found above the test binary.");
    }
}
