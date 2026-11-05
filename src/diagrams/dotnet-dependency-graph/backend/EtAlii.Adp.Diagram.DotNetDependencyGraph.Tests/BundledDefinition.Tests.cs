using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "dotnet-dependency-graph";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("dotnet-dependency-graph", Diagram.DependencyGraph.Origin.Key, "net.etalii.adp.dotnet.dependency-graph")
        {
            Title = Diagram.DependencyGraph.Title,
            Icon = Diagram.DependencyGraph.Icon,
        },
    ];

    /// <summary>
    /// Persistence is the module's FBL binding, without the <c>files</c> DISL 11.2 forbids beside it (finding A1), and
    /// its id pattern admits today's ids, which DISL 11.5's default does not (finding A3).
    /// </summary>
    [Fact]
    public void ThePersistence_IsFbl_WithoutFiles_AndAPatternThatAdmitsTodaysIds()
    {
        // Arrange.
        var persistence = Module.ResourceJson("dotnet-dependency-graph.dis").GetProperty("persistence");
        var project = "project:src/Pipeline.Core/Pipeline.Core.csproj";
        var reference = $"depends:{project}->project:{new string('a', 200)}/Deep.csproj";

        // Act.
        var pattern = new Regex(persistence.GetProperty("ids").GetProperty("pattern").GetString()!);

        // Assert.
        Assert.Equal("fbl", persistence.GetProperty("format").GetString());
        Assert.False(persistence.TryGetProperty("files", out _), "persistence.files is beside format fbl.");
        Assert.Matches(pattern, project);
        Assert.Matches(pattern, "package:Serilog");
        Assert.Matches(pattern, reference);
        Assert.DoesNotMatch(new Regex("^[A-Za-z0-9_.:#-]{1,128}$"), reference);
    }

    /// <summary>
    /// An operation a plugin carries out is handed back with the plugin's <c>args</c> as the definition
    /// writes them (DISL §9.3, <c>plugin: {name, args}</c>), so the host knows which action it is asked for.
    /// </summary>
    [Fact]
    public void TheRevealProjectFileOperation_HandsBackThePluginWithItsArguments()
    {
        // Arrange.
        var specification = BundledDefinition.Load(Module, "dotnet-dependency-graph.dis").Specification;
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
