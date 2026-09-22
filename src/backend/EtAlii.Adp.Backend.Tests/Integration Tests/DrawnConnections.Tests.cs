using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.AnsibleStructure;
using EtAlii.Adp.Diagram.AzurePipeline;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.CausalLoop;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.DependencyGraph;
using EtAlii.Adp.Diagram.DotNetDependencyGraph;
using EtAlii.Adp.Diagram.HelmCharts;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Diagram.Sparql;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Diagram.WardleyMap;
using EtAlii.Adp.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// <b>Every connection any canvas draws resolves to a selection</b>, checked against the real composed
/// host over every module's shipped examples (centralized-selection task 26).
/// </summary>
/// <remarks>
/// <para>
/// One test over the host rather than one per module test project, by the task's ruling: the host is
/// the composition the application runs, so every <em>other</em> module's resolver answering "not mine"
/// to a foreign id is exercised too, which a module's isolated provider cannot show.
/// </para>
/// <para>
/// <b>The types are each mapper's own constants, never literals.</b> A type the examples never emit is
/// not named: naming it would measure the corpus rather than the resolvers, and the union check still
/// fails the day such an element appears, so someone must name it then.
/// </para>
/// </remarks>
public class DrawnConnectionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DrawnConnectionsTests(WebApplicationFactory<Program> baseFactory) =>
        _factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("developer"));

    /// <summary>
    /// Every module whose canvas the examples show, with the types its projection emits over them.
    /// </summary>
    private static readonly DrawnModule[] Modules =
    [
        new("ansible-structure", new(
            [AnsibleElementMapper.EdgeType],
            [AnsibleElementMapper.PlaybookType, AnsibleElementMapper.PlayType, AnsibleElementMapper.RoleType, AnsibleElementMapper.TaskFileType, AnsibleElementMapper.InventoryType, AnsibleElementMapper.VariableFolderType])),
        // The three levels nest: a stage's jobs and their arrows exist only while the stage is open, and
        // a job's steps only while the job is too. So every stage and every job now drawn is opened, and
        // the helper calls this again with what that revealed - without it the job graph's arrows and
        // every step would never be visited at all.
        new("azure-pipeline", new(
            [PipelineElementMapper.EdgeType],
            [PipelineElementMapper.StageType, PipelineElementMapper.JobType, PipelineElementMapper.StepType, PipelineElementMapper.TemplateType]),
            ExpandViews: (services, view) =>
            {
                var views = services.GetRequiredService<PipelineViewState>();
                var open = views.For(view.WatchId, view.BodyPath);
                var openable = new[] { PipelineElementMapper.StageType, PipelineElementMapper.JobType };
                foreach (var element in view.Baseline.Where(element => openable.Contains(element.Type)))
                {
                    // Only what is still shut. Toggle FLIPS, so re-toggling an open stage on the next
                    // round would close it and the expansion would oscillate rather than settle - which
                    // is exactly what the helper's round cap reported when this hook first ran.
                    //
                    // One set of ids for both levels since Developer 2's job-steps change: a job's id
                    // carries its stage's, so the view state takes either without being told which.
                    if (!open.IsExpanded(element.Id))
                    {
                        views.Toggle(view.WatchId, view.BodyPath, element.Id);
                    }
                }
            }),
        new("c4", new(
            [C4ElementMapper.RelationshipType],
            [C4ElementMapper.NodeType, C4ElementMapper.BoundaryType, C4ElementMapper.ViewType])),
        new("causal-loop", new(
            [CausalLoopElementMapper.LinkType],
            [CausalLoopElementMapper.VariableType, CausalLoopElementMapper.LoopType])),
        new("databricks", new(
            [DatabricksElementMapper.EdgeType, DatabricksElementMapper.OverrideEdgeType, DatabricksElementMapper.FlowEdgeType],
            [DatabricksElementMapper.TaskType, DatabricksElementMapper.ClusterType, DatabricksElementMapper.BundleType, DatabricksElementMapper.ResourceType, DatabricksElementMapper.TargetType, DatabricksElementMapper.PipelineNodeType])),
        new("dependency-graph", new(
            [DependencyGraphElementMapper.RelationType],
            [DependencyGraphElementMapper.NodeType])),
        new("dotnet-dependency-graph", new(
            [DependencyElementMapper.EdgeType],
            [DependencyElementMapper.ProjectType, DependencyElementMapper.PackageType])),
        new("helm-charts", new(
            [HelmElementMapper.EdgeType, HelmElementMapper.DependencyType],
            [HelmElementMapper.ChartType, HelmElementMapper.ValuesType, HelmElementMapper.SchemaType, HelmElementMapper.TemplateType, HelmElementMapper.PartialType, HelmElementMapper.SubchartType, HelmElementMapper.LockType])),
        new("mindmap", new(
            [],
            [MindmapElementMapper.NodeType],
            NoConnectionsBecause: "a branch is drawn client-side from each node's parentId; the projection emits nodes only")),
        new("owl", new(
            [OwlElementMapper.EdgeType],
            [OwlElementMapper.NodeType, OwlElementMapper.ExpressionType])),
        new("rdf", new(
            [RdfElementMapper.EdgeType],
            [RdfElementMapper.ResourceType, RdfElementMapper.TruncationType])),
        new("shacl", new(
            [ShaclElementMapper.EdgeType],
            [ShaclElementMapper.ShapeType])),
        new("skos", new(
            [SkosElementMapper.EdgeType],
            [SkosElementMapper.ConceptType, SkosElementMapper.SchemeType, SkosElementMapper.TruncationType])),
        new("sparql", new(
            [SparqlElementMapper.EdgeType],
            [SparqlElementMapper.VariableType, SparqlElementMapper.TermType, SparqlElementMapper.RegionType, SparqlElementMapper.AnnotationType, SparqlElementMapper.HeaderType])),
        new("timeline", new(
            [TimelineElementMapper.ConnectionType],
            [TimelineElementMapper.PeriodType, TimelineElementMapper.MomentType])),
        new("wardley-map", new(
            [WardleyElementTypes.Link],
            [WardleyElementTypes.Element, WardleyElementTypes.Note, WardleyElementTypes.Annotation, WardleyElementTypes.Accelerator, WardleyElementTypes.Attitude, WardleyElementTypes.EvolutionAxis])),
    ];

    [Fact]
    public async Task EveryConnectionEveryCanvasDraws_ResolvesToASelection()
    {
        // Arrange.
        var problems = new List<string>();

        // Act.
        foreach (var module in Modules)
        {
            problems.AddRange(await DrawnConnections.ProblemsAsync(_factory.Services, module));
        }

        // Assert.
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryShippedExampleFolder_IsChecked()
    {
        // Arrange: discovered by artifact, not by a second list - a module that ships examples and is
        // missing from Modules would otherwise never be checked. With both canaries: a floor on the
        // folders walked, and a member that must be among them.
        var folders = Directory.EnumerateDirectories(IoPath.Combine(DrawnConnections.ExamplesRoot(), "diagrams"))
            .Select(IoPath.GetFileName)
            .ToList();

        // Act.
        var unchecked_ = folders.Where(folder => Modules.All(module => module.ExamplesFolder != folder)).ToList();

        // Assert.
        Assert.True(folders.Count >= 16, $"walked only {folders.Count} example folders: {string.Join(", ", folders)}");
        Assert.Contains("timeline", folders);
        Assert.True(unchecked_.Count == 0, "example folders no module entry checks: " + string.Join(", ", unchecked_));
    }

    [Fact]
    public void EveryDiagramTypeTheHostOpens_HasItsExamplesChecked()
    {
        // Arrange: the other direction, from the composition itself - every session factory the host
        // registers is a canvas that draws, and must be among the types the checked examples open.
        var services = _factory.Services;
        var opened = services.GetServices<IDiagramSessionFactory>().Select(factory => factory.Origin.MimeType).ToHashSet(StringComparer.Ordinal);

        // Act.
        var examined = new HashSet<string>(StringComparer.Ordinal);
        var router = services.GetRequiredService<Hierarchy.DiagramFileRouter>();
        foreach (var module in Modules)
        {
            var folder = IoPath.Combine(DrawnConnections.ExamplesRoot(), "diagrams", module.ExamplesFolder);
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (router.Route(file) is Hierarchy.DiagramRouted routed)
                {
                    examined.Add(routed.Definition.Origin.MimeType);
                }
            }
        }
        var unexamined = opened.Where(type => !examined.Contains(type)).Order(StringComparer.Ordinal).ToList();

        // Assert.
        // Both canaries, so this cannot pass by enumerating nothing: a floor on the factories seen, and a
        // member that must be among them.
        Assert.True(opened.Count >= 16, $"the host registers only {opened.Count} session factories");
        Assert.Contains(global::EtAlii.Adp.Diagram.Timeline.Diagram.Timeline.Origin.MimeType, opened);
        Assert.True(unexamined.Count == 0, "diagram types the host opens that no checked example opens: " + string.Join(", ", unexamined));
    }
}
