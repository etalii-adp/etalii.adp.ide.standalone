using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The store (Requirement 7.1): one graph per solution, kept true as the files it was derived from
/// change, through the shared watched-folder contract - keyed by the solution rather than its
/// folder, and watching the files the solution names rather than everything under it.
/// </summary>
public sealed class DependencyGraphStoreTests : WatchedFolderStoreContract<DependencyGraphModel>
{
    protected override WatchedFolderStore<DependencyGraphModel> CreateStore(TimeSpan settleDelay, TimeProvider time) =>
        new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache")), settleDelay, time: time);

    /// <summary>A solution of one centrally managed project, its version in the props file at the root.</summary>
    protected override void Populate(string root)
    {
        File.WriteAllText(Path.Combine(root, "Solution.slnx"), """<Solution><Project Path="Library/Library.csproj" /></Solution>""");
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), Props("4.4.0"));
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        File.WriteAllText(Path.Combine(root, "Library", "Library.csproj"), Project(""));
    }

    protected override string SubjectOf(string root) => Path.Combine(root, "Solution.slnx");

    /// <summary>The project takes a second package.</summary>
    protected override Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(root, "Library", "Library.csproj"), Project("""<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />"""), cancellationToken);

    protected override bool ShowsTheChange(DependencyGraphModel snapshot) =>
        snapshot.Packages.Any(package => package.PackageId == "Newtonsoft.Json");

    /// <summary>A restore or a build rewriting the project file is the real-world shape of a burst.</summary>
    protected override Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(root, "Library", "Library.csproj"), Project($"<!-- {index} -->"), cancellationToken);

    [Fact]
    public async Task AVersionBumpInTheCentralPropsFile_IsHeard()
    {
        // Arrange: in a centrally managed repository a version bump lands in a props file and in no
        // .csproj at all, so a store watching only the solution and its projects would miss it.
        var cancellationToken = TestContext.Current.CancellationToken;
        Store.GetOrLoad(Subject);
        var heard = new TaskCompletionSource<DependencyGraphModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = Store.Watch(Subject, graph =>
        {
            if (graph.Packages.Any(package => package.Versions.Contains("4.5.0")))
            {
                heard.TrySetResult(graph);
            }
        });

        // Act.
        await File.WriteAllTextAsync(Path.Combine(Root, "Directory.Packages.props"), Props("4.5.0"), cancellationToken);

        // Assert.
        Assert.True(await SettlesAsync(() => heard.Task.IsCompleted, cancellationToken), "The version bump was not heard.");
        var graph = await heard.Task;
        Assert.Equal(["4.5.0"], Assert.Single(graph.Packages, package => package.PackageId == "Serilog").Versions);
    }

    private static string Props(string serilogVersion) => $"""
        <Project>
          <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
          <ItemGroup><PackageVersion Include="Serilog" Version="{serilogVersion}" /></ItemGroup>
        </Project>
        """;

    private static string Project(string more) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup><PackageReference Include="Serilog" />{more}</ItemGroup>
        </Project>
        """;
}
