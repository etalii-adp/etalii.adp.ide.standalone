using System.Collections.Concurrent;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// Making a project, a package or a reference selectable inside a solution's graph, through the
/// registration the tab was opened at - and refusing a selection that cannot be verified.
/// </summary>
public sealed class DotNetContextSourceResolverTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly TestFolder _testFolder = TestFolder.Create("ddg-resolver");
    private readonly string _root;
    private readonly FakeTimeProvider _clock = new();
    private readonly DependencyGraphStore _store;
    private readonly DotNetContextSourceResolver _resolver;
    private readonly string _solution;
    private readonly string _registration;

    public DotNetContextSourceResolverTests()
    {
        _root = _testFolder.Path;
        _store = new DependencyGraphStore(
            new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(IoPath.Combine("no", "such", "cache")), SettleDelay, time: _clock);
        Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Library\Library.csproj" /></ItemGroup>
            </Project>
            """);
        Write("Library/Library.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Serilog" Version="4.4.0" /></ItemGroup>
            </Project>
            """);
        _solution = Write("Solution.slnx", """
            <Solution>
              <Project Path="App/App.csproj" />
              <Project Path="Library/Library.csproj" />
            </Solution>
            """);
        _registration = Write("Solution.adp", "dotnet/dependency-graph\nbody: Solution.slnx\n");
        _resolver = new DotNetContextSourceResolver(new DiagramFileRouter(new DotNetOnlyCatalog()), _store, new DependencyElementMapper());
    }

    public void Dispose()
    {
        _store.Dispose();

        _testFolder.Dispose();
    }

    [Fact]
    public async Task AProject_ResolvesToItsPathAndName_WithThePayloadTheCanvasWasSent()
    {
        // Arrange.
        var sent = new DependencyElementMapper().Elements(_store.GetOrLoad(_solution))
            .Single(element => element.Id == "project:App/App.csproj");

        // Act: selected inside the registration, as a tab opened at the .adp selects.
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("project:App/App.csproj")).Level;

        // Assert: the target is the solution the registration names, which the property provider reads.
        Assert.Equal(["App", "App.csproj"], level.RelativePath);
        Assert.Equal("App", level.Detail.Element.Text);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal(IoPath.GetFullPath(_solution), IoPath.GetFullPath(level.Target.ResolvedFullPath));
        Assert.Equal(sent.Type, level.Detail.Element.ElementType);
        Assert.Equal(sent.Payload.ToArray(), level.Detail.Element.Payload.Value.ToByteArray());
    }

    [Fact]
    public async Task APackage_AnswersAsItsId_AndAReferenceAsItsKind()
    {
        // Arrange.
        var edge = _store.GetOrLoad(_solution).Edges.Single(candidate => candidate.Kind == DependsOnKind.Project);

        // Act.
        var package = Assert.IsType<ResolvedContextLevel>(await Resolve("package:Serilog")).Level;
        var reference = Assert.IsType<ResolvedContextLevel>(await Resolve(edge.Id)).Level;

        // Assert.
        Assert.Equal(["Serilog"], package.RelativePath);
        Assert.Equal("Serilog", package.Detail.Element.Text);
        Assert.Equal([edge.Id], reference.RelativePath);
        Assert.Equal("ProjectReference", reference.Detail.Element.Text);
    }

    [Fact]
    public async Task AnUnknownElement_IsRejected()
    {
        // Act.
        var rejected = Assert.IsType<RejectedContextLevel>(await Resolve("project:Nowhere/Nowhere.csproj"));

        // Assert.
        Assert.Equal("Unknown element.", rejected.Reason);
    }

    [Fact]
    public async Task AFileOfAnotherType_IsRejectedPlainly()
    {
        // Arrange.
        var other = Write("notes.txt", "Not a diagram.");

        // Act.
        var rejected = Assert.IsType<RejectedContextLevel>(await Resolve("project:App/App.csproj", file: other));

        // Assert.
        Assert.Equal("The selected file is not a .NET dependency graph.", rejected.Reason);
    }

    [Fact]
    public async Task AClientPathThatDisagrees_IsRejected()
    {
        // Act: the client's version of the path is checked, never trusted.
        var resolution = await Resolve("project:App/App.csproj", clientPath: ["Library", "Library.csproj"]);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task ARemovedProject_ClearsATrackedSelection_UntilItIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var level = Assert.IsType<ResolvedContextLevel>(await Resolve("project:Library/Library.csproj")).Level;
        var cleared = new TaskCompletionSource();
        var heard = new ConcurrentQueue<IReadOnlyList<string>?>();
        var tracking = _resolver.Track(level.Target.WatchId, ProjectRoot.From(_root), level, path =>
        {
            heard.Enqueue(path);
            if (path is null)
            {
                cleared.TrySetResult();
            }
        });

        // Act: the solution stops naming the project.
        await File.WriteAllTextAsync(_solution, "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n", cancellationToken);
        Assert.True(await Eventually.TrueAsync(() => Settled(() => cleared.Task.IsCompleted), cancellationToken, WaitLimit), "The selection was not cleared.");
        tracking.Dispose();
        var heardBefore = heard.Count;
        await File.WriteAllTextAsync(_solution, "<Solution>\n</Solution>\n", cancellationToken);

        // Assert: nothing is heard once the selection is let go.
        Assert.False(await Eventually.TrueAsync(() => Settled(() => heard.Count != heardBefore), cancellationToken, Silence), "A selection was heard after it was let go.");
    }

    /// <summary><paramref name="probe"/>, asked once the store's clock has moved a settle delay.</summary>
    private T Settled<T>(Func<T> probe)
    {
        _clock.Advance(SettleDelay);
        return probe();
    }

    private string Write(string relativePath, string content)
    {
        var full = IoPath.Combine(_root, relativePath);
        Directory.CreateDirectory(IoPath.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Resolves <paramref name="elementId"/> as selected inside <paramref name="file"/>, the registration by default.</summary>
    private Task<ContextLevelResolution> Resolve(string elementId, IReadOnlyList<string>? clientPath = null, string? file = null)
    {
        file ??= _registration;
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            [IoPath.GetFileName(file)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, file, IsContainer: false, SourceId: default, ProjectRoot.From(_root), ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        return _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), ProjectRoot.From(_root), new ContextSource { ElementId = new ElementId { Value = elementId } },
            clientPath ?? [], parent, TestContext.Current.CancellationToken).AsTask();
    }

    /// <summary>
    /// A catalog carrying only this module's definition, so the router routes the registration
    /// without the process-wide discovery cache being filled by a test.
    /// </summary>
    private sealed class DotNetOnlyCatalog : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
    }
}
