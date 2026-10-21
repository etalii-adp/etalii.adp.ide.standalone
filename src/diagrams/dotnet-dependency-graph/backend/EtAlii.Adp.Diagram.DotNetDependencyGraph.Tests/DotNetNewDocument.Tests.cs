using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// A new .NET dependency graph, as core's Add creates it: the registration and a solution made from the
/// binding's <c>template</c>, under the extension Q4 rules, never over an existing file, and read through the
/// binding with no failure (dotnet-dependency-graph-disl-fbl Requirement 5.3, finding B11).
/// </summary>
/// <remarks>
/// Each test drives the real Add of <see cref="AddDiagramContextActionProvider"/> over the module's own
/// registrations and the real command pipeline, so what is asserted is the file a user gets. Before task 9 the
/// new document was <c>New.sln</c> holding the <c>.slnx</c> serialization, which the classic pattern reads as
/// nothing at all; the pull request of task 9 records each test failing against that code.
/// </remarks>
public sealed class DotNetNewDocumentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddg-new-document-").FullName;

    private readonly List<DependencyGraphStore> _stores = [];

    [Fact]
    public async Task ANewDocument_IsASlnx_MadeFromTheBindingsTemplate_AndOpenedByItsRegistration()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act.
        var result = await AddAsync("New", cancellationToken);
        var body = DiagramFilePair.BodyOf(Path.Combine(_root, "New.adp"), Catalog, _root);

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.NotNull(body);
        Assert.Equal(Path.Combine(_root, "New.slnx"), body.Value.Path);
        Assert.Equal(DotNetBody.Binding.Template!.Text, await File.ReadAllTextAsync(body.Value.Path, cancellationToken));
        Assert.Equal(["New.adp", "New.slnx"], Listing());
    }

    [Fact]
    public async Task ANewDocument_ReadsThroughTheBinding_WithNoFailure_AndHoldsNoProject()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync("New", cancellationToken);
        var body = DiagramFilePair.BodyOf(Path.Combine(_root, "New.adp"), Catalog, _root)!.Value.Path;

        // Act.
        var read = Open(body);

        // Assert.
        Assert.False(read.Reading.Unreadable);
        Assert.Empty(read.Reading.Findings);
        Assert.Empty(read.Graph.Failures);
        Assert.Empty(read.Graph.Projects);
    }

    [Fact]
    public async Task ANewDocument_IsReadInItsOwnSerialization_SoAProjectAddedToItIsDrawn()
    {
        // Arrange: a project added the way the .NET tooling adds one to this serialization. Read by the
        // pattern of another serialization, as the classic reading of XML is, it would be no project at all.
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync("New", cancellationToken);
        var body = DiagramFilePair.BodyOf(Path.Combine(_root, "New.adp"), Catalog, _root)!.Value.Path;
        Directory.CreateDirectory(Path.Combine(_root, "App"));
        await File.WriteAllTextAsync(Path.Combine(_root, "App", "App.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""", cancellationToken);
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        await File.WriteAllTextAsync(body, text.Replace("</Solution>", "  <Project Path=\"App/App.csproj\" />\r\n</Solution>", StringComparison.Ordinal), cancellationToken);

        // Act.
        var read = Open(body);

        // Assert.
        Assert.Equal(["project:App/App.csproj"], read.Graph.Projects.Select(project => project.Id));
        Assert.Empty(read.Graph.Failures);
    }

    [Fact]
    public async Task ANewDocument_IsNeverWrittenOverAnExistingSolution()
    {
        // Arrange: a solution already at the name the new document would take.
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = Path.Combine(_root, "New.slnx");
        await File.WriteAllTextAsync(existing, "<Solution>\r\n  <Project Path=\"Kept/Kept.csproj\" />\r\n</Solution>\r\n", cancellationToken);
        var before = await File.ReadAllBytesAsync(existing, cancellationToken);

        // Act.
        var result = await AddAsync("New", cancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal("An item named 'New.slnx' already exists in this folder.", result.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(existing, cancellationToken));
        Assert.Equal(["New.slnx"], Listing());
    }

    [Fact]
    public async Task ANewDocument_BesideAClassicSolutionOfTheSameName_IsRefused_AndLeavesThatSolutionAlone()
    {
        // Arrange: a registration without a body: header beside both serializations would open the new
        // one, so a name either serialization already holds is refused, as today's Add refuses it.
        var cancellationToken = TestContext.Current.CancellationToken;
        var classic = Path.Combine(_root, "New.sln");
        await File.WriteAllTextAsync(classic, "\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\nGlobal\r\nEndGlobal\r\n", cancellationToken);
        var before = await File.ReadAllBytesAsync(classic, cancellationToken);

        // Act.
        var result = await AddAsync("New", cancellationToken);

        // Assert.
        Assert.False(result.Completed);
        Assert.Equal("An item named 'New.sln' already exists in this folder.", result.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(classic, cancellationToken));
        Assert.Equal(["New.sln"], Listing());
    }

    private static DiagramDefinitionCatalog Catalog { get; } = new() { All = [Diagram.DependencyGraph] };

    /// <summary>Core's Add of a new .NET dependency graph named <paramref name="name"/> in the test's folder, over the module's registrations.</summary>
    private async Task<ContextCommitResult> AddAsync(string name, CancellationToken cancellationToken)
    {
        var factories = new DiagramDocumentFactories(new ServiceCollection().AddDotNetDependencyGraph().BuildServiceProvider().GetServices<IDiagramDocumentFactory>());
        var history = TestHistory.For(_ => { });
        var provider = new AddDiagramContextActionProvider(history, factories, Catalog, new DiagramFileRouter(Catalog));
        var folder = new ContextTarget(ContextScope.Hierarchy, _root, IsContainer: true, ShortGuid.NewShortGuid(), Root: ProjectRoot.From(_root));
        return await provider.CommitAsync(folder, AddDiagramContextActionProvider.AddActionId, Diagram.DependencyGraph.Origin.Key, name, cancellationToken);
    }

    private DotNetBody Open(string solution)
    {
        var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache")));
        _stores.Add(store);
        return store.BodyOf(store.GetOrLoad(solution));
    }

    private string[] Listing() => [.. Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        TestFolder.TryDelete(_root);
    }
}
