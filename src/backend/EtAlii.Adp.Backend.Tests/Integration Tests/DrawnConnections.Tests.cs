using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.AgentActivityDiagram;
using EtAlii.Adp.Diagram.AgentBehaviorModelling;
using EtAlii.Adp.Diagram.AnsibleStructure;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.CausalLoopDiagram;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.DependencyGraph;
using EtAlii.Adp.Diagram.DotNetDependencyGraph;
using EtAlii.Adp.Diagram.FunctionalDecompositionGraph;
using EtAlii.Adp.Diagram.GartnerHypeCycleGraph;
using EtAlii.Adp.Diagram.HelmChart;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Diagram.Sankey;
using EtAlii.Adp.Diagram.Sparql;
using EtAlii.Adp.Diagram.SupplyChain;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Diagram.WardleyMap;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path;
// `Diagram.Timeline` would read as the namespace rather than the module's Diagram class, so the class is aliased.
using TimelineModule = EtAlii.Adp.Diagram.Timeline.Diagram;

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

    private readonly string _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));

    public DrawnConnectionsTests(WebApplicationFactory<Program> baseFactory) =>
        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // The problem cache lives and dies with this test rather than in the real user
                // profile the host's AddProblems registration points at - the leak
                // ProblemStoreIsolationTests exists to stop, and which it caught here.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });

    /// <summary>
    /// Opens every stage and job the pipeline drew that is still shut - shared with
    /// <see cref="ShippedExampleModelsTests"/>, which exports what a fully opened pipeline sends.
    /// </summary>
    internal static void ExpandPipelineViews(IServiceProvider services, DrawnView view)
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
    }

    /// <summary>
    /// Every module whose canvas the examples show, with the types its projection emits over them.
    /// </summary>
    private static readonly DrawnModule[] Modules =
    [
        new("agent-behavior-modelling", new DrawnTypes(
            [AbmElementMapper.ChildType],
            [AbmElementMapper.SequenceType, AbmElementMapper.FallbackType, AbmElementMapper.ParallelType, AbmElementMapper.RetryType, AbmElementMapper.RepeatType, AbmElementMapper.GuardType, AbmElementMapper.ApprovalType, AbmElementMapper.CheckType, AbmElementMapper.ActionType, AbmElementMapper.AskType, AbmElementMapper.DelegateType])),
        new("ansible-structure", new DrawnTypes(
            [AnsibleElementMapper.EdgeType],
            [AnsibleElementMapper.PlaybookType, AnsibleElementMapper.PlayType, AnsibleElementMapper.RoleType, AnsibleElementMapper.TaskFileType, AnsibleElementMapper.InventoryType, AnsibleElementMapper.VariableFolderType])),
        // The three levels nest: a stage's jobs and their arrows exist only while the stage is open, and
        // a job's steps only while the job is too. So every stage and every job now drawn is opened, and
        // the helper calls this again with what that revealed - without it the job graph's arrows and
        // every step would never be visited at all.
        new("azure-devops-pipeline", new DrawnTypes(
            [PipelineElementMapper.EdgeType],
            [PipelineElementMapper.StageType, PipelineElementMapper.JobType, PipelineElementMapper.StepType, PipelineElementMapper.TemplateType]),
            ExpandViews: ExpandPipelineViews),
        new("c4", new DrawnTypes(
            [C4ElementMapper.RelationshipType],
            [C4ElementMapper.NodeType, C4ElementMapper.BoundaryType, C4ElementMapper.ViewType])),
        new("causal-loop-diagram", new DrawnTypes(
            [CausalLoopElementMapper.LinkType],
            [CausalLoopElementMapper.VariableType, CausalLoopElementMapper.LoopType])),
        new("databricks", new DrawnTypes(
            [DatabricksElementMapper.EdgeType, DatabricksElementMapper.OverrideEdgeType, DatabricksElementMapper.FlowEdgeType],
            [DatabricksElementMapper.TaskType, DatabricksElementMapper.ClusterType, DatabricksElementMapper.BundleType, DatabricksElementMapper.ResourceType, DatabricksElementMapper.TargetType, DatabricksElementMapper.PipelineNodeType])),
        new("dependency-graph", new DrawnTypes(
            [DependencyGraphElementMapper.RelationType],
            [DependencyGraphElementMapper.NodeType])),
        new("dotnet-dependency-graph", new DrawnTypes(
            [DependencyElementMapper.EdgeType],
            [DependencyElementMapper.ProjectType, DependencyElementMapper.PackageType])),
        new("functional-decomposition-graph", new DrawnTypes(
            [FdgElementMapper.UiChildType, FdgElementMapper.OwnsActionType, FdgElementMapper.OwnsDataType, FdgElementMapper.OwnsFunctionType, FdgElementMapper.ShowsType],
            [FdgElementMapper.UiElementType, FdgElementMapper.DataElementType, FdgElementMapper.ActionType, FdgElementMapper.FunctionType, FdgElementMapper.CommentType])),
        new("gartner-hype-cycle-graph", new DrawnTypes(
            [GhgElementMapper.InfluenceType],
            [GhgElementMapper.TrendType, GhgElementMapper.TriggerType, GhgElementMapper.NoteType])),
        new("agent-activity-diagram", new DrawnTypes(
            [AadElementMapper.RelationType],
            [AadElementMapper.ProjectType, AadElementMapper.SpecificationType, AadElementMapper.AgentType, AadElementMapper.LocationType, AadElementMapper.EnvironmentType, AadElementMapper.ViewType])),
        new("helm-chart", new DrawnTypes(
            [HelmElementMapper.EdgeType, HelmElementMapper.DependencyType],
            [HelmElementMapper.ChartType, HelmElementMapper.ValuesType, HelmElementMapper.SchemaType, HelmElementMapper.TemplateType, HelmElementMapper.PartialType, HelmElementMapper.SubchartType, HelmElementMapper.LockType])),
        new("mindmap", new DrawnTypes(
            [],
            [MindmapElementMapper.NodeType],
            NoConnectionsBecause: "a branch is drawn client-side from each node's parentId; the projection emits nodes only")),
        new("owl", new DrawnTypes(
            [OwlElementMapper.EdgeType],
            [OwlElementMapper.NodeType, OwlElementMapper.ExpressionType])),
        new("rdf", new DrawnTypes(
            [RdfElementMapper.EdgeType],
            [RdfElementMapper.ResourceType, RdfElementMapper.TruncationType])),
        new("sankey", new DrawnTypes(
            [SankeyElementMapper.FlowType],
            [SankeyElementMapper.NodeType])),
        new("shacl", new DrawnTypes(
            [ShaclElementMapper.EdgeType],
            [ShaclElementMapper.ShapeType])),
        new("skos", new DrawnTypes(
            [SkosElementMapper.EdgeType],
            [SkosElementMapper.ConceptType, SkosElementMapper.SchemeType, SkosElementMapper.TruncationType])),
        new("sparql", new DrawnTypes(
            [SparqlElementMapper.EdgeType],
            [SparqlElementMapper.VariableType, SparqlElementMapper.TermType, SparqlElementMapper.RegionType, SparqlElementMapper.AnnotationType, SparqlElementMapper.HeaderType])),
        new("supply-chain", new DrawnTypes(
            [SupplyChainElementMapper.FlowType],
            [SupplyChainElementMapper.GroupType, SupplyChainElementMapper.SourceType, SupplyChainElementMapper.ProcessorType, SupplyChainElementMapper.ProducerType, SupplyChainElementMapper.IntegratorType, SupplyChainElementMapper.HubType, SupplyChainElementMapper.OutletType, SupplyChainElementMapper.ConsumerType])),
        new("timeline", new DrawnTypes(
            [TimelineElementMapper.ConnectionType],
            [TimelineElementMapper.PeriodType, TimelineElementMapper.MomentType])),
        new("wardley-map", new DrawnTypes(
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
        var unclaimed = folders.Where(folder => Modules.All(module => module.ExamplesFolder != folder)).ToList();

        // Assert.
        Assert.True(folders.Count >= 16, $"walked only {folders.Count} example folders: {string.Join(", ", folders)}");
        Assert.Contains("timeline", folders);
        Assert.True(unclaimed.Count == 0, "example folders no module entry checks: " + string.Join(", ", unclaimed));
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
        var router = services.GetRequiredService<DiagramFileRouter>();
        foreach (var module in Modules)
        {
            var folder = IoPath.Combine(DrawnConnections.ExamplesRoot(), "diagrams", module.ExamplesFolder);
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (router.Route(file) is DiagramRouted routed)
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
        Assert.Contains(TimelineModule.Timeline.Origin.MimeType, opened);
        Assert.True(unexamined.Count == 0, "diagram types the host opens that no checked example opens: " + string.Join(", ", unexamined));
    }
}
