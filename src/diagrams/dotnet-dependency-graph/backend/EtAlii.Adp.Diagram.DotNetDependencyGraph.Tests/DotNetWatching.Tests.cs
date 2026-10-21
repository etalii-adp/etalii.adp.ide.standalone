using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// What a reading of a solution depends on is watched (Requirement 4, finding B15): the two changes a watch over only
/// the files read would miss, a project a wildcard <c>Include</c> begins to match and a versions file created above a
/// project after the diagram opened, each seen to fail against the store's earlier watch first.
/// </summary>
/// <remarks>
/// The settle runs on <see cref="Clock"/>, which a test moves while it waits for the file system, so no test waits
/// out a debounce in real time and a loaded runner costs patience, never a verdict (code-structure FR-051).
/// </remarks>
public sealed class DotNetWatchingTests : IDisposable
{
    /// <summary>How long the file system is given to report a change.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.DotNetWatching");

    private FakeTimeProvider Clock { get; } = new();

    private DependencyGraphStore Store => field ??= new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache")), SettleDelay, time: Clock);

    public void Dispose()
    {
        Store.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task AProjectAWildcardBeginsToMatch_IsReadAgain()
    {
        // Arrange: the host takes every project under mods/ by a wildcard, and the solution names one that is not there
        // yet, in a folder the wildcard already lists. Only the wildcard's folder can hear it arrive: the file was never
        // read, so it is in no list of files read.
        var cancellationToken = TestContext.Current.CancellationToken;
        var solution = Write("Solution.slnx", """
            <Solution>
              <Project Path="Host/Host.csproj" />
              <Project Path="mods/B/B.csproj" />
            </Solution>
            """);
        Write("Host/Host.csproj", Project("""<ProjectReference Include="..\mods\*\*.csproj" />"""));
        Directory.CreateDirectory(_folder.Combine("mods", "B"));
        Assert.DoesNotContain(Store.GetOrLoad(solution).Projects, project => project.RelativePath == "mods/B/B.csproj");

        // Act.
        Write("mods/B/B.csproj", Project(""));

        // Assert.
        Assert.True(
            await SettlesAsync(() => Store.GetOrLoad(solution).Edges.Any(edge => edge is { FromElementId: "project:Host/Host.csproj", ToElementId: "project:mods/B/B.csproj" }), cancellationToken),
            "The project the wildcard began to match was not read.");
    }

    [Fact]
    public async Task AVersionsFileCreatedAboveAProjectLater_IsReadAgain()
    {
        // Arrange: a project that takes its version centrally, with no versions file anywhere above it yet.
        var cancellationToken = TestContext.Current.CancellationToken;
        var solution = Write("Solution.slnx", """<Solution><Project Path="Library/Library.csproj" /></Solution>""");
        Write("Library/Library.csproj", Project("""<PackageReference Include="Serilog" />"""));
        Assert.DoesNotContain("4.4.0", Assert.Single(Store.GetOrLoad(solution).Packages).Versions);

        // Act: the versions file is created beside the solution, above the project.
        Write("Directory.Packages.props", """
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup><PackageVersion Include="Serilog" Version="4.4.0" /></ItemGroup>
            </Project>
            """);

        // Assert.
        Assert.True(
            await SettlesAsync(() => Store.GetOrLoad(solution).Packages.Any(package => package.Versions.Contains("4.4.0")), cancellationToken),
            "The versions file created above the project was not read.");
    }

    [Fact]
    public void TheWatch_NamesEveryProjectRead_EveryFolderAWildcardListed_AndAVersionsFileInEveryFolderAboveAProject()
    {
        // Arrange: a host that takes mods/*/*.csproj by a wildcard, beside a project that will not parse.
        var solution = Write("Solution.slnx", """
            <Solution>
              <Project Path="Host/Host.csproj" />
              <Project Path="Broken/Broken.csproj" />
            </Solution>
            """);
        var host = Write("Host/Host.csproj", Project("""<ProjectReference Include="..\mods\*\*.csproj" />"""));
        var broken = Write("Broken/Broken.csproj", "<Project");
        Write("mods/A/A.csproj", Project(""));

        // Act.
        var watched = DotNetBody.Open(solution, new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache"))).WatchPaths;

        // Assert: the unreadable project stays watched so its return is read, and no versions file exists anywhere here.
        Assert.Contains(host, watched);
        Assert.Contains(broken, watched);
        Assert.Contains(_folder.Combine("mods"), watched);
        Assert.Contains(_folder.Combine("mods", "A"), watched);
        for (var folder = new DirectoryInfo(_folder.Combine("Host")); folder is not null; folder = folder.Parent)
        {
            Assert.Contains(Path.Combine(folder.FullName, "Directory.Packages.props"), watched);
        }

        Assert.DoesNotContain(solution, watched);
        Assert.Equal(watched.Count, watched.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheSettle_IsTheBindings_250Milliseconds()
    {
        // Requirement 4.2: stated in the binding, which FBL gives a settle to folder bodies only (finding B15), and handed
        // to the shared watcher by the module.
        Assert.Equal(250, DotNetBody.Binding.Body.Settle);
        Assert.Equal(TimeSpan.FromMilliseconds(250), DotNetSolutionSubject.Settle);
    }

    /// <summary>Whether <paramref name="condition"/> came to hold, moving the store's clock a settle delay before each look.</summary>
    private async Task<bool> SettlesAsync(Func<bool> condition, CancellationToken cancellationToken) =>
        await Eventually.TrueAsync(() =>
        {
            Clock.Advance(SettleDelay);
            return condition();
        }, cancellationToken, WaitLimit);

    private string Write(string relativePath, string content)
    {
        var full = _folder.Combine(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static string Project(string items) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup>{items}</ItemGroup>
        </Project>
        """;
}
