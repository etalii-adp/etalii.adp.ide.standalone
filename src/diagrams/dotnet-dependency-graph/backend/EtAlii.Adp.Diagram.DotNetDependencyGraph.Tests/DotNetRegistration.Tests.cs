using System.Text;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// A move of a box writes the registration's <c>layout:</c> block through the library's
/// registration writer, as one undo step that restores the registration byte for byte
/// (dotnet-dependency-graph-disl-fbl Requirements 5.1 and 5.4), over the real command pipeline the
/// host builds.
/// </summary>
/// <remarks>
/// What tells the library's writer from core's is what finding B14 measured, and these tests hold
/// the answers recommended for it: an id goes where the byte order of its UTF-8 encoding puts it,
/// a new line takes the registration's own line ending, and an undo over a registration that changed
/// on disk since the move is refused with the library's sentence rather than written over it.
/// </remarks>
public sealed class DotNetRegistrationTests : IDisposable
{
    /// <summary>Holds U+1D538, which UTF-16 orders before U+FF21 and UTF-8 after it.</summary>
    private const string Astral = "project:\U0001D538/\U0001D538.csproj";

    private const string Fullwidth = "project:Ａ/Ａ.csproj";

    private readonly string _root = Directory.CreateTempSubdirectory("ddg-registration-").FullName;

    private readonly List<DependencyGraphStore> _stores = [];

    private static string Fixtures { get; } = RepositoryPaths.Under(
        "src", "diagrams", "dotnet-dependency-graph", "backend", "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests", "Fixtures");

    [Fact]
    public async Task AMove_WritesTheEntryWhereTheLibraryOrdersIt_AndItsUndoRestoresTheBytes()
    {
        // Arrange: the registration of finding B14, which holds the fullwidth id.
        var cancellationToken = TestContext.Current.CancellationToken;
        TestFolder.CopyTree(Path.Combine(Fixtures, "registration-order"), _root);
        var registration = Path.Combine(_root, "Order.adp");
        var before = await File.ReadAllBytesAsync(registration, cancellationToken);
        var history = History();
        var session = Session(Path.Combine(_root, "Order.slnx"), registration, history);

        // Act.
        var error = await session.MoveElementToAsync(Astral, 30, 40, cancellationToken);
        var moved = await File.ReadAllTextAsync(registration, cancellationToken);
        var undo = await history.UndoAsync(cancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal($"dotnet/dependency-graph\r\nbody: Order.slnx\r\nlayout:\r\n  {Fullwidth}: 10 20\r\n  {Astral}: 30 40\r\n", moved);
        Assert.True(undo.IsSuccess, undo.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(registration, cancellationToken));
    }

    [Fact]
    public async Task AFirstMove_IntoARegistrationOfLfLines_WritesLfLines_AndItsUndoRestoresTheBytes()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        (string solution, string registration) = Seed("dotnet/dependency-graph\nbody: Solution.slnx\n");
        var history = History();
        var session = Session(solution, registration, history);

        // Act.
        var error = await session.MoveElementToAsync("project:App/App.csproj", 120.5, -0.0001, cancellationToken);
        var moved = await File.ReadAllTextAsync(registration, cancellationToken);
        await history.UndoAsync(cancellationToken);

        // Assert.
        Assert.Equal("", error);
        Assert.Equal("dotnet/dependency-graph\nbody: Solution.slnx\nlayout:\n  project:App/App.csproj: 120.5 0\n", moved);
        Assert.Equal("dotnet/dependency-graph\nbody: Solution.slnx\n", await File.ReadAllTextAsync(registration, cancellationToken));
    }

    [Fact]
    public async Task AnUndo_AfterTheRegistrationChangedOnDisk_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        (string solution, string registration) = Seed("dotnet/dependency-graph\r\nbody: Solution.slnx\r\nlayout:\r\n  project:Library/Library.csproj: 10 20\r\n");
        var history = History();
        var session = Session(solution, registration, history);
        await session.MoveElementToAsync("project:App/App.csproj", 1, 2, cancellationToken);
        var edited = (await File.ReadAllTextAsync(registration, cancellationToken)).Replace("10 20", "11 21", StringComparison.Ordinal);
        await File.WriteAllTextAsync(registration, edited, cancellationToken);

        // Act.
        var undo = await history.UndoAsync(cancellationToken);

        // Assert.
        Assert.False(undo.IsSuccess);
        Assert.Equal(SplicedFile.DriftUndo, undo.Error);
        Assert.Equal(edited, await File.ReadAllTextAsync(registration, cancellationToken));
    }

    [Fact]
    public async Task AMoveAndItsRedo_GiveTheSameBytes()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        (string solution, string registration) = Seed("dotnet/dependency-graph\r\nbody: Solution.slnx\r\n");
        var history = History();
        var session = Session(solution, registration, history);
        await session.MoveElementToAsync("project:App/App.csproj", 5, 6, cancellationToken);
        var moved = await File.ReadAllBytesAsync(registration, cancellationToken);

        // Act.
        await history.UndoAsync(cancellationToken);
        var redo = await history.RedoAsync(cancellationToken);

        // Assert.
        Assert.True(redo.IsSuccess, redo.Error);
        Assert.Equal(moved, await File.ReadAllBytesAsync(registration, cancellationToken));
    }

    [Fact]
    public async Task AMove_OfADocumentOpenedWithoutARegistration_IsRefusedWithTodaysSentence()
    {
        // Arrange.
        (string solution, _) = Seed("dotnet/dependency-graph\r\nbody: Solution.slnx\r\n");
        var session = Session(solution, null, History());

        // Act.
        var error = await session.MoveElementToAsync("project:App/App.csproj", 1, 2, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("This diagram was opened without a registration, so there is nowhere to store a position.", error);
    }

    /// <summary>The history of a project in a host whose command pipeline is the real one.</summary>
    private IHistoryStack History() => TestHistory.For(_ => { }).Get(ProjectRoot.From(_root));

    private (string Solution, string Registration) Seed(string registration)
    {
        Directory.CreateDirectory(Path.Combine(_root, "App"));
        File.WriteAllText(Path.Combine(_root, "App", "App.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
        var solution = Path.Combine(_root, "Solution.slnx");
        File.WriteAllText(solution, """<Solution><Project Path="App/App.csproj" /></Solution>""");
        var adp = Path.Combine(_root, "Solution.adp");
        File.WriteAllBytes(adp, Encoding.UTF8.GetBytes(registration));
        return (solution, adp);
    }

    private DotNetDependencyGraphSession Session(string solution, string? registration, IHistoryStack history)
    {
        var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache")));
        _stores.Add(store);
        return new DotNetDependencyGraphSession(solution, store, new DependencyElementMapper(), registration, history, watch: false);
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        TestFolder.TryDelete(_root);
    }
}
