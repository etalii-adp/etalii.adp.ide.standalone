using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The persistence plugin of <c>helm-chart.fbl#chart</c> (helm-chart-disl-fbl, Requirements 3.2 to 3.6): what it
/// delivers from the files it is handed, the two relations that may have an open end, and that it reads from
/// those files alone and never plans.
/// </summary>
/// <remarks>
/// The cases of <c>Resolves</c>, <c>Includes</c>, the node types, the archive, undeclared content and edge
/// identity were <c>HelmGraph.Tests</c>' and moved here with the reading (task 9); the empty graph of a folder
/// that is not a chart is the folder body's answer since then (<see cref="AFolderThatIsNotAChart_IsAnEmptyModel"/>).
/// </remarks>
public class HelmChartFolderPluginTests
{
    [Fact]
    public void TheReading_IsMadeFromTheHandedFilesAlone()
    {
        // Arrange: a chart that exists nowhere but in memory, so a plugin that looked anything up on disk would
        // find none of it.
        var request = Request(
            ("Chart.yaml", "apiVersion: v2\nname: in-memory\nversion: 1.2.3\ndependencies:\n  - name: redis\n    version: 1.x\n"),
            ("values.yaml", "replicaCount: 1\n"),
            ("values.schema.json", "{}"),
            ("templates/_helpers.tpl", "{{- define \"mem.name\" -}}x{{- end }}\n"),
            ("templates/deployment.yaml", "kind: Deployment\nname: {{ include \"mem.name\" . }}\n"),
            ("charts/redis/Chart.yaml", "apiVersion: v2\nname: redis\nversion: 1.0.0\n"),
            ("Chart.lock", "dependencies:\n  - name: redis\n    version: 1.0.0\n"));
        request = request with { SupplementaryEntries = ["charts/redis/", "crds/"] };

        // Act.
        var read = new HelmChartFolderPlugin().Read(request);

        // Assert.
        Assert.False(read.Unreadable);
        Assert.Empty(read.Findings);
        var chart = Assert.Single(read.Elements, element => element.Type == "Chart");
        Assert.Equal("in-memory", chart.Attributes.GetValueOrDefault("name"));
        Assert.Equal(
            ["chart", "values:values.yaml", "schema:values.schema.json", "tpl:templates/_helpers.tpl", "tpl:templates/deployment.yaml", "crds", "sub:charts/redis", "dep:redis", "lock:Chart.lock"],
            read.Elements.Where(element => !element.IsRelation).Select(element => element.Id));
        Assert.Contains(read.Elements, element => element is { Type: "Resolves", Source: "dep:redis", Target: "sub:charts/redis" });
        Assert.Contains(read.Elements, element => element is { Type: "Includes", Source: "tpl:templates/deployment.yaml", Target: "tpl:templates/_helpers.tpl" });
    }

    [Fact]
    public void Resolves_LandsOnTheVendoredEntry_OrHasNoTarget()
    {
        // Arrange & Act.
        var elements = WellFormed();

        // Assert.
        // redis is vendored (matched via the chart name even though the alias is "cache");
        // postgres is Unvendored - a relation without a target, not a finding.
        var resolves = elements.Where(element => element.Type == "Resolves").ToArray();
        Assert.Contains(resolves, element => element is { Source: "dep:cache", Target: "sub:charts/redis" });
        Assert.Contains(resolves, element => element is { Source: "dep:postgres", Target: null });
    }

    [Fact]
    public void Includes_LandsOnTheDefiningPartial()
    {
        // Arrange & Act.
        var elements = WellFormed();

        // Assert.
        var includes = elements
            .Where(element => element is { Type: "Includes", Source: "tpl:templates/deployment.yaml" })
            .ToArray();
        Assert.Equal(2, includes.Length);
        Assert.All(includes, element => Assert.Equal("tpl:templates/_helpers.tpl", element.Target));
        Assert.Equal(["shop.fullname", "shop.labels"], includes.Select(element => (string)element.Attributes["label"]!).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AnIncludeNoLocalPartialDefines_IsARelationWithoutATarget()
    {
        // Arrange.
        // A library chart provides these at render time, so the reference is a marked
        // unknown - the relation keeps its identity through the label (R5.6).
        var request = Request(
            ("Chart.yaml", "apiVersion: v2\nname: sample\nversion: 1.0.0\n"),
            ("templates/deployment.yaml", "metadata:\n  labels: {{ include \"common.labels\" . }}\n"));

        // Act.
        var elements = new HelmChartFolderPlugin().Read(request).Elements;

        // Assert.
        var open = Assert.Single(elements, element => element.Type == "Includes");
        Assert.Null(open.Target);
        Assert.Equal("common.labels", open.Attributes["label"]);
        Assert.Contains("common.labels", open.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNodeType_AppearsForTheWellFormedFixture()
    {
        // Arrange & Act.
        var types = WellFormed().Where(element => !element.IsRelation).Select(element => element.Type).Distinct().ToArray();

        // Assert.
        Assert.Contains("Chart", types);
        Assert.Contains("Values", types);
        Assert.Contains("Schema", types);
        Assert.Contains("Template", types);
        Assert.Contains("Partial", types);
        Assert.Contains("Crds", types);
        Assert.Contains("Dependency", types);
        Assert.Contains("Subchart", types);
        Assert.Contains("Lock", types);
    }

    [Fact]
    public void ASealedArchive_IsAnArchiveNode()
    {
        // Arrange & Act.
        var elements = Read("unconventional").Elements;

        // Assert.
        var archive = Assert.Single(elements, element => element.Type == "Archive");
        Assert.Equal("sealed", archive.Attributes["entryName"]);
        Assert.Equal("charts/sealed-9.9.9.tgz", archive.Attributes["path"]);
    }

    [Fact]
    public void UndeclaredVendoredContent_IsDrawnAndResolvedByNothing()
    {
        // Arrange & Act.
        // The unconventional fixture declares only "deep"; the sealed archive is undeclared -
        // still a node, and no dependency resolves to it, which the definition's rule warns about.
        var elements = Read("unconventional").Elements;

        // Assert.
        Assert.Contains(elements, element => element.Id == "tgz:charts/sealed-9.9.9.tgz");
        Assert.DoesNotContain(elements, element => element is { Type: "Resolves", Target: "tgz:charts/sealed-9.9.9.tgz" });
    }

    [Fact]
    public void TwoIncludesOfDifferentNames_OnOnePartial_AreTwoRelationsWithTwoIds()
    {
        // Arrange & Act.
        // The composite id (target AND label): the mapper found the collision when both
        // includes of deployment.yaml keyed to the same partial - this pins the fix.
        var relations = WellFormed().Where(element => element.IsRelation).ToArray();

        // Assert.
        var includes = relations.Where(element => element.Type == "Includes").Select(element => element.Id).ToArray();
        Assert.Equal(includes.Length, includes.Distinct(StringComparer.Ordinal).Count());
        // And across every relation the plugin delivers: element identity is what the wire and the layout store.
        var all = relations.Select(element => element.Id).ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AFolderThatIsNotAChart_IsAnEmptyModel()
    {
        // Arrange & Act: the folder body never hands a folder that fails recognise to the plugin.
        var reading = HelmFolder.Reading(HelmFolder.Fixture("not-a-chart"));

        // Assert.
        Assert.False(reading.IsChart);
        Assert.Empty(reading.Disl.Diagram.Nodes);
        Assert.Empty(reading.Disl.Diagram.Relations);
    }

    [Fact]
    public void AFileThatDoesNotParse_OrIsHandedWithoutBytes_CostsThatFilesDetailAndNothingElse()
    {
        // Arrange: a values layer YamlDotNet refuses, and a template larger than the host reads.
        var request = Request(
            ("Chart.yaml", "apiVersion: v2\nname: sample\nversion: 1.0.0\n"),
            ("values.yaml", "a: [unclosed\n"),
            ("templates/deployment.yaml", "kind: Deployment\n"));
        request = request with
        {
            Files = [.. request.Files, new PluginFile("templates/huge.yaml", []) { IsOversized = true }],
        };

        // Act.
        var read = new HelmChartFolderPlugin().Read(request);

        // Assert: every node is still there, the refused file is one finding at its own file, and the oversized
        // template is a node without facts.
        Assert.Equal(
            ["chart", "values:values.yaml", "tpl:templates/deployment.yaml", "tpl:templates/huge.yaml"],
            read.Elements.Where(element => !element.IsRelation).Select(element => element.Id));
        var finding = Assert.Single(read.Findings);
        Assert.Equal(FindingCodes.Unparseable, finding.Code);
        Assert.Equal("values.yaml", finding.Location?.File);
        Assert.StartsWith("'values.yaml' is not readable YAML: ", finding.Message, StringComparison.Ordinal);
        var huge = Assert.Single(read.Elements, element => element.Id == "tpl:templates/huge.yaml");
        Assert.Empty((IEnumerable<object?>)huge.Attributes["kinds"]!);
    }

    [Fact]
    public void APlan_IsRefusedWithTheBindingsSentence()
    {
        // Arrange.
        var plugin = new HelmChartFolderPlugin();
        var request = new PluginPlanRequest([], new PluginReadResult([], [], false), new ModelChange.Remove("chart"));

        // Act.
        var plan = plugin.Plan(request);

        // Assert.
        var refused = Assert.IsType<PluginPlanResult.Refused>(plan);
        Assert.Equal(HelmDefinition.Binding.ReadOnly, refused.Reason);
    }

    [Fact]
    public void ATemplate_IsNeverAskedFor()
    {
        // Act and assert.
        Assert.Throws<InvalidOperationException>(() =>
            new HelmChartFolderPlugin().Template(new PluginTemplateRequest("chart", new Dictionary<string, string>())));
    }

    private static IReadOnlyList<FblElement> WellFormed() => Read("well-formed").Elements;

    private static PluginReadResult Read(string fixture) =>
        new HelmChartFolderPlugin().Read(HelmFolder.Request(HelmFolder.Fixture(fixture)));

    private static PluginReadRequest Request(params (string Path, string Text)[] files) =>
        new([.. files.Select(file => new PluginFile(file.Path, Encoding.UTF8.GetBytes(file.Text)))]);
}
