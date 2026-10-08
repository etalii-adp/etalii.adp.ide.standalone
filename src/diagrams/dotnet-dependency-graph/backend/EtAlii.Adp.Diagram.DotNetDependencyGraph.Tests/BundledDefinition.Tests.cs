using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads.
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the
/// file on disk - so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh dotnet-dependency-graph &lt;etalii.adp checkout&gt;</c>, never by hand.
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
        var provenance = Json("dotnet-dependency-graph.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Resource("dotnet-dependency-graph.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = Json("dotnet-dependency-graph.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/dotnet-dependency-graph.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its language id, and its origin, title and icon equal the catalog's.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = Json("dotnet-dependency-graph.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.dotnet.dependency-graph", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.DependencyGraph.Origin.Key, language.GetProperty("origin").GetString());
        Assert.Equal(Diagram.DependencyGraph.Title, language.GetProperty("label").GetString());
        Assert.Equal(Diagram.DependencyGraph.Icon, language.GetProperty("icon").GetString());
    }

    /// <summary>The runtime loads the definition with no error and no warning, so nothing in it is silently not read.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutDiagnostics()
    {
        var loaded = BundledDefinition.Load(_module, "dotnet-dependency-graph.dis");

        Assert.Empty(loaded.Diagnostics);
    }

    /// <summary>
    /// An operation a plugin carries out is handed back with the plugin's <c>args</c> as the definition
    /// writes them (DISL §9.3, <c>plugin: {name, args}</c>), so the host knows which action it is asked for.
    /// </summary>
    [Fact]
    public void TheRevealProjectFileOperation_HandsBackThePluginWithItsArguments()
    {
        // Arrange.
        var specification = BundledDefinition.Load(_module, "dotnet-dependency-graph.dis").Specification;
        var diagram = new DislDiagram(specification);
        var project = diagram.AddNode("Project", "p", new Dictionary<string, object?> { ["name"] = "App", ["path"] = "src/App/App.csproj" });

        // Act.
        var transaction = OperationInterpreter.Run(specification, "revealProjectFile", diagram, project, DislIds.Fixed());

        // Assert.
        Assert.Null(transaction.Refusal);
        var plugin = Assert.IsType<HostAction.Plugin>(Assert.Single(transaction.HostActions));
        Assert.Equal("net.etalii.adp.dotnet.solution", plugin.Name);
        Assert.Equal("revealProjectFile", Assert.Single(plugin.Arguments, a => a.Key == "action").Value);
    }
}
