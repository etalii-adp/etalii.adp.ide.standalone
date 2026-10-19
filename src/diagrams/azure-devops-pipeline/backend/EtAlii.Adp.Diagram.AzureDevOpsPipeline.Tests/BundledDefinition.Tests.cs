using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, it loads, and it
/// says what azure-devops-pipeline-disl-fbl Requirement 2 asks of it (findings A1 to A4).
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the file on disk,
/// so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh azure-devops-pipeline &lt;etalii.adp checkout&gt;</c>, never by hand. The toolbox,
/// the context menus and the property rows are derived from it (<see cref="PipelineDefinition"/>).
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private const string Definition = "azure-devops-pipeline.dis";
    private const string Provenance = "azure-devops-pipeline.provenance.json";

    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The sha256 in provenance.json is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        var provenance = _module.ResourceJson(Provenance);

        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes(Definition)));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of <c>definition/</c>, so what is reviewed is what is loaded.</summary>
    [Fact]
    public async Task TheEmbeddedBytes_AreTheBundledFile()
    {
        var file = IoPath.Combine(PipelineCorpus.ModuleFolder, "definition", Definition);

        var onDisk = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(onDisk, _module.ResourceBytes(Definition));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = _module.ResourceJson(Provenance);

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/azure-devops-pipeline.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its language id, its origin the catalog origin, at DISL 0.3 (finding A1).</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage_AtDisl03()
    {
        var root = _module.ResourceJson(Definition);
        var language = root.GetProperty("language");

        Assert.Equal("0.3", root.GetProperty("disl").GetString());
        Assert.Equal("net.etalii.adp.azure-devops.pipeline", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.Pipeline.Origin.Key, language.GetProperty("origin").GetString());
    }

    /// <summary>The definition loads through the DISL library with no diagnostic of severity error (Requirement 2.5).</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutAnError()
    {
        var loaded = DislLoader.Load(_module.ResourceBytes(Definition));

        Assert.NotNull(loaded.Specification);
        Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
    }

    /// <summary>
    /// Persistence is the module's FBL binding, without the keys DISL 11.2 forbids beside it (finding A3), and the ids
    /// are derived paths (finding A8).
    /// </summary>
    [Fact]
    public void ThePersistence_IsTheModulesBinding_WithoutTheKeysAPluginFormatCarried()
    {
        var persistence = _module.ResourceJson(Definition).GetProperty("persistence");

        Assert.Equal("fbl", persistence.GetProperty("format").GetString());
        Assert.EndsWith("/src/diagrams/azure-devops-pipeline/backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline/azure-devops-pipeline.fbl#pipeline", persistence.GetProperty("binding").GetString(), StringComparison.Ordinal);
        Assert.True(persistence.TryGetProperty("typeMap", out _));
        foreach (var forbidden in new[] { "encoding", "mediaType", "omitDefaults" })
        {
            Assert.False(persistence.TryGetProperty(forbidden, out _), $"persistence.{forbidden} is beside format fbl.");
        }
    }

    /// <summary>
    /// <c>x-azure-pipeline</c> names every tool, action and property id the parity transcript holds (finding A4), so a
    /// derivation can map every answer onto today's wire ids.
    /// </summary>
    [Fact]
    public async Task TheWireIdBlock_NamesEveryIdTheTranscriptHolds()
    {
        var block = _module.ResourceJson(Definition).GetProperty("x-azure-pipeline");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in block.GetProperty("tools").EnumerateObject())
        {
            declared.Add(tool.Value.GetProperty("id").GetString()!);
            declared.Add(tool.Value.GetProperty("drop").GetString()!);
        }

        foreach (var map in new[] { "actions", "properties" })
        {
            declared.UnionWith(block.GetProperty(map).EnumerateObject().Select(entry => entry.Value.GetString()!));
        }

        using var transcript = JsonDocument.Parse(await File.ReadAllBytesAsync(PipelineTranscript.CheckedInPath, TestContext.Current.CancellationToken));
        var held = TranscriptIds(transcript.RootElement);

        Assert.True(held.Count >= 20, $"Only {held.Count} wire ids were read from the transcript; the reading has stopped finding them.");
        Assert.Empty(held.Where(id => !declared.Contains(id)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The module's binding, <c>azure-devops-pipeline.fbl</c>, loads with no problem of severity error, and its reader is
    /// the persistence plugin (Requirement 4.1, Q2).
    /// </summary>
    [Fact]
    public void TheBinding_Loads_AndItsReaderIsThePlugin()
    {
        var problems = FblDocumentLoader.Load(_module.ResourceBytes("azure-devops-pipeline.fbl"), out var document);

        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var binding = Assert.Single(document!.Bindings).Value;
        Assert.Equal(PipelineYamlPlugin.PluginId, binding.Plugin?.Plugin);
        Assert.Equal(binding.Plugin?.Plugin, new PipelineYamlPlugin(PipelineReadContext.Unbound).Id);
    }

    /// <summary>
    /// The binding's template is the bytes a new pipeline is created with (finding B17): the factory's, and the
    /// transcript's <c>emptyDocument</c>, CRLF, blank lines and final newline included.
    /// </summary>
    [Fact]
    public async Task TheBindingsTemplate_IsTheFactorysBytes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHistoryStackStore>(new HistoryStackStore(new PipelineTestDispatcher(new PipelineDocumentStore(new FileWriter()))));
        services.AddAzurePipeline();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin == Diagram.Pipeline.Origin);
        using var transcript = JsonDocument.Parse(await File.ReadAllBytesAsync(PipelineTranscript.CheckedInPath, TestContext.Current.CancellationToken));

        Assert.Equal(transcript.RootElement.GetProperty("emptyDocument").GetString(), PipelineBody.NewDocument("parity"));
        foreach (var name in new[] { "parity", "azure-pipelines", "build.ci", "" })
        {
            Assert.Equal(factory.CreateEmptyDocument(name), PipelineBody.NewDocument(name));
        }
    }

    /// <summary>The wire ids the transcript holds: the toolbox's ids and drops, the menus' action ids and the rows' property ids.</summary>
    private static HashSet<string> TranscriptIds(JsonElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in root.GetProperty("toolbox").EnumerateArray())
        {
            var fields = tool.GetString()!.Split(" | ");
            ids.Add(fields[0]);
            ids.Add(fields[3]["drops ".Length..]);
        }

        foreach (var document in root.GetProperty("documents").EnumerateArray())
        {
            if (document.TryGetProperty("menus", out var menus))
            {
                // A menu line is "g<n> <action id> | label | icon | shortcut | state".
                ids.UnionWith(menus.EnumerateArray().SelectMany(menu => menu.EnumerateArray()).Select(line => line.GetString()!.Split(" | ")[0].Split(' ')[1]));
            }

            if (document.TryGetProperty("rows", out var rows))
            {
                ids.UnionWith(rows.EnumerateArray().SelectMany(target => target.GetProperty("rows").EnumerateArray()).Select(line => line.GetString()!.Split(" | ")[0]));
            }
        }

        return ids;
    }
}
