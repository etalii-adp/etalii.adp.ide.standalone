using System.Reflection;
using System.Text.Json;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is, it loads, and it
/// says what azure-devops-pipeline-disl-fbl Requirement 2 asks of it (findings A1 to A4).
/// </summary>
/// <remarks>
/// The toolbox, the context menus and the property rows are derived from it (<see cref="PipelineDefinition"/>).
/// </remarks>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    private const string Definition = "azure-devops-pipeline.dis";

    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "azure-devops-pipeline";

    /// <summary>At DISL 0.3 (finding A1).</summary>
    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("azure-devops-pipeline", Diagram.Pipeline.Origin.Key, "net.etalii.adp.azure-devops.pipeline") { Disl = "0.3" }];

    /// <summary>Requirement 2.5 asks for no diagnostic of severity error; a warning is allowed.</summary>
    protected override bool AWarningIsAllowed => true;

    /// <summary>
    /// Persistence is the module's FBL binding, without the keys DISL 11.2 forbids beside it (finding A3), and the ids
    /// are derived paths (finding A8).
    /// </summary>
    [Fact]
    public void ThePersistence_IsTheModulesBinding_WithoutTheKeysAPluginFormatCarried()
    {
        var persistence = Module.ResourceJson(Definition).GetProperty("persistence");

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
        var block = Module.ResourceJson(Definition).GetProperty("x-azure-pipeline");
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

        using var transcript = JsonDocument.Parse(await File.ReadAllBytesAsync(new PipelineTranscript().CheckedInPath, TestContext.Current.CancellationToken));
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
        var problems = FblDocumentLoader.Load(Module.ResourceBytes("azure-devops-pipeline.fbl"), out var document);

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
        services.AddSingleton(TestHistory.For(s => s.AddAzurePipeline()));
        services.AddAzurePipeline();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin == Diagram.Pipeline.Origin);
        using var transcript = JsonDocument.Parse(await File.ReadAllBytesAsync(new PipelineTranscript().CheckedInPath, TestContext.Current.CancellationToken));

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
