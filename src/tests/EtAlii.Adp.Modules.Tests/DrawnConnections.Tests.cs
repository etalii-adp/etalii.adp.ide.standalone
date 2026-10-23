using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Sessions;
using EtAlii.Adp.TestSupport.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;
// `Diagram.Timeline` would read as the namespace rather than the module's Diagram class, so the class is aliased.
using TimelineModule = EtAlii.Adp.Diagram.Timeline.Diagram;

namespace EtAlii.Adp.Modules.Tests;

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
/// <b>Nothing is listed by hand but what no constant can say.</b> Every folder under
/// <c>src/examples/diagrams</c> is checked, with the types <see cref="WireTypes"/> reads off the
/// modules whose diagram types its examples open: a constant marked <see cref="RelationAttribute"/>
/// is a connection, every other one an element. Only a module's view hook and a "no connections"
/// statement are kept here, keyed by examples folder.
/// </para>
/// </remarks>
public class DrawnConnectionsTests(HostFixture host) : IClassFixture<HostFixture>
{
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
    /// The modules whose canvas draws more after a view change than on open, by examples folder. The
    /// pipeline's three levels nest: a stage's jobs and their arrows exist only while the stage is open,
    /// and a job's steps only while the job is too. So every stage and every job now drawn is opened,
    /// and the helper calls this again with what that revealed - without it the job graph's arrows and
    /// every step would never be visited at all.
    /// </summary>
    private static readonly Dictionary<string, Action<IServiceProvider, DrawnView>> ViewExpansions = new(StringComparer.Ordinal)
    {
        ["azure-devops-pipeline"] = ExpandPipelineViews,
    };

    /// <summary>The modules that declare no relation, by examples folder, each with the reason in words.</summary>
    private static readonly Dictionary<string, string> NoConnectionsBecause = new(StringComparer.Ordinal)
    {
        ["mindmap"] = "a branch is drawn client-side from each node's parentId; the projection emits nodes only",
    };

    [Fact]
    public async Task EveryConnectionEveryCanvasDraws_ResolvesToASelection()
    {
        // Arrange.
        var modules = Modules();
        var problems = new List<string>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        // Act.
        foreach (var module in modules)
        {
            problems.AddRange(await DrawnConnections.ProblemsAsync(host.Services, module, emitted));
        }

        // Every relation a module declares is one some example draws, so every one was resolved above.
        var undrawn = WireTypes.All.Where(type => type.IsRelation && !emitted.Contains(type.Value)).Select(type => type.Where).Order(StringComparer.Ordinal);
        problems.AddRange(undrawn.Select(where => $"{where} is marked [Relation], but no shipped example draws it, so whether it resolves is never checked."));

        // Assert.
        // Both canaries, so this cannot pass by walking nothing: a floor on the folders walked, and a
        // member that must be among them. And every hook names a folder that is walked, so a renamed
        // folder cannot silently drop its hook.
        Assert.True(modules.Count >= 16, $"walked only {modules.Count} example folders: {string.Join(", ", modules.Select(module => module.ExamplesFolder))}");
        Assert.Contains(modules, module => module.ExamplesFolder == "timeline");
        var folders = modules.Select(module => module.ExamplesFolder).ToHashSet(StringComparer.Ordinal);
        Assert.All(ViewExpansions.Keys.Concat(NoConnectionsBecause.Keys), folder => Assert.Contains(folder, folders));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryDiagramTypeTheHostOpens_HasItsExamplesChecked()
    {
        // Arrange: the other direction, from the composition itself - every session factory the host
        // registers is a canvas that draws, and must be among the types the checked examples open.
        var services = host.Services;
        var opened = services.GetServices<IDiagramSessionFactory>().Select(factory => factory.Origin.MimeType).ToHashSet(StringComparer.Ordinal);

        // Act.
        var examined = new HashSet<string>(StringComparer.Ordinal);
        var router = services.GetRequiredService<DiagramFileRouter>();
        foreach (var folder in ExampleFolders())
        {
            examined.UnionWith(OriginsOpenedIn(router, folder).Select(origin => origin.MimeType));
        }
        var unexamined = opened.Where(type => !examined.Contains(type)).Order(StringComparer.Ordinal).ToList();

        // Assert.
        // Both canaries, so this cannot pass by enumerating nothing: a floor on the factories seen, and a
        // member that must be among them.
        Assert.True(opened.Count >= 16, $"the host registers only {opened.Count} session factories");
        Assert.Contains(TimelineModule.Timeline.Origin.MimeType, opened);
        Assert.True(unexamined.Count == 0, "diagram types the host opens that no checked example opens: " + string.Join(", ", unexamined));
    }

    /// <summary>
    /// Every folder under <c>src/examples/diagrams</c>, with the types of the modules its examples open
    /// and the hooks kept above.
    /// </summary>
    private List<DrawnModule> Modules()
    {
        var router = host.Services.GetRequiredService<DiagramFileRouter>();

        // Which module declares each diagram type: the module whose assembly discovery finds it in.
        var moduleOf = WireTypes.Assemblies()
            .Select(assembly => (Module: WireTypes.ModuleOf(assembly), Assembly: assembly))
            .Where(pair => pair.Module.Length > 0)
            .SelectMany(pair => DiagramDefinitionDiscovery.Discover([pair.Assembly]).Select(definition => (definition.Origin, pair.Module)))
            .ToDictionary(pair => pair.Origin, pair => pair.Module);

        var modules = new List<DrawnModule>();
        foreach (var folder in ExampleFolders())
        {
            var owners = OriginsOpenedIn(router, folder)
                .Select(origin => moduleOf.GetValueOrDefault(origin, ""))
                .ToHashSet(StringComparer.Ordinal);
            var types = WireTypes.All.Where(type => owners.Contains(type.Module)).ToList();
            var name = IoPath.GetFileName(folder);
            modules.Add(new DrawnModule(
                name,
                new DrawnTypes(
                    [.. types.Where(type => type.IsRelation).Select(type => type.Value).Distinct(StringComparer.Ordinal)],
                    [.. types.Where(type => !type.IsRelation).Select(type => type.Value).Distinct(StringComparer.Ordinal)],
                    NoConnectionsBecause.GetValueOrDefault(name)),
                ViewExpansions.GetValueOrDefault(name)));
        }
        return modules;
    }

    private static IEnumerable<string> ExampleFolders() =>
        Directory.EnumerateDirectories(IoPath.Combine(DrawnConnections.ExamplesRoot(), "diagrams")).Order(StringComparer.Ordinal);

    private static IEnumerable<ToolOrigin> OriginsOpenedIn(DiagramFileRouter router, string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => router.Route(file))
            .OfType<DiagramRouted>()
            .Select(routed => routed.Definition.Origin)
            .Distinct();
}
