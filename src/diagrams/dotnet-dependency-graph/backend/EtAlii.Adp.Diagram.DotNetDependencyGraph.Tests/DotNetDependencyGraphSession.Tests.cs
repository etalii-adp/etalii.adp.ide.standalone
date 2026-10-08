using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The session against real files in a temporary solution: elements out, a position in, and a
/// refresh that keeps the arrangement. These are the integration tests the design asks for -
/// they read from disk exactly as the backend will, because the binding and the layout overlay
/// are precisely the parts a unit test with stub readings could not have exercised.
/// </summary>
public sealed class DotNetDependencyGraphSessionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddg-session-").FullName;

    /// <summary>
    /// Stands in for core's <c>SetRegistrationLayoutCommandHandler</c>, applying the one command
    /// this module dispatches so that a stored position genuinely reaches the file - which is
    /// what makes these integration tests rather than assertions about a mock.
    /// </summary>
    /// <remarks>
    /// <c>ICommand</c> is a marker: a real stack resolves a handler for it. Standing in for that
    /// handler here keeps the test's subject THIS module - does it dispatch the right command,
    /// against the registration, for the right element - while the handler's own correctness and
    /// its inverse stay core's, where <c>SetRegistrationLayoutCommand.Tests</c> already owns
    /// them. Undo and redo are therefore inert here rather than half-implemented.
    /// </remarks>
    private sealed class DirectHistoryStack : IHistoryStack
    {
        /// <summary>Every command this stack was asked to run, so a test can name what was dispatched.</summary>
        private List<ICommand> Executed { get; } = [];

        public bool CanUndo => false;

        private bool CanRedo => false;

        private int UndoCount => 0;

        private int RedoCount => 0;

        public HistoryAvailability Availability => new(CanUndo, CanRedo, UndoCount, RedoCount);

        public event EventHandler? Changed;

        public Task<CommandResult> ExecuteAsync(ICommand command, CancellationToken cancellationToken = default)
        {
            Executed.Add(command);

            if (command is SetRegistrationLayoutCommand layout)
            {
                RegistrationLayout.SetPosition(layout.AdpPath, layout.ElementId, new RegistrationPosition(layout.X, layout.Y));
            }

            // AFTER the write, as HistoryStack.ExecuteAsync does - it raises Changed once the
            // command has run and been recorded. This double used to raise it first, which was
            // harmless while nothing listened and would have made a listener read the OLD file.
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(CommandResult.Success());
        }

        /// <summary>
        /// What the real stack does after an undo or a redo has rewritten the file: say so. Lets
        /// a test put a position back the way an inverse would and then announce it, without
        /// this double growing a second copy of undo.
        /// </summary>
        public void AnnounceChange() => Changed?.Invoke(this, EventArgs.Empty);

        public Task<CommandResult> UndoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResult.Success());

        public Task<CommandResult> RedoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CommandResult.Success());
    }

    private string Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>A two-project solution: App references Library, Library consumes Serilog.</summary>
    private (string Solution, string Registration) Seed()
    {
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
        var solution = Write("Solution.slnx", """
            <Solution>
              <Project Path="App/App.csproj" />
              <Project Path="Library/Library.csproj" />
            </Solution>
            """);
        var registration = Write("Solution.adp", "dotnet/dependency-graph\nbody: Solution.slnx\n");
        return (solution, registration);
    }

    /// <summary>
    /// A session with watching DECLINED, deliberately. A test asserting a refresh through a real
    /// FileSystemWatcher would be asserting the file system's timing rather than this module's
    /// behaviour: the settle delay, the platform's own event coalescing and the runner's
    /// scheduling would all be inside the assertion. Refresh() is called directly instead -
    /// which is exactly what the watcher calls - and SolutionWatcher's wiring is tested apart.
    /// </summary>
    private static DotNetDependencyGraphSession SessionFor(string solution, string? registration, IHistoryStack? history = null) =>
        new(
            solution,
            new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache"))),
            new DependencyElementMapper(),
            registration,
            history,
            watch: false);

    [Fact]
    public void Baseline_DrawsEveryProjectPackageAndEdge()
    {
        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration);

        // Act.
        var delta = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()));

        // Assert.
        Assert.Contains(delta.Elements, element => element.Id == "project:App/App.csproj");
        Assert.Contains(delta.Elements, element => element.Id == "project:Library/Library.csproj");
        Assert.Contains(delta.Elements, element => element.Id == "package:Serilog");
        Assert.Equal(2, delta.Elements.Count(element => element.Type == DependencyElementMapper.EdgeType));
    }

    [Fact]
    public async Task AReposition_IsStoredInTheRegistration_AndNowhereElse()
    {
        // Requirements 6.1, 6.5 and the correctness rule: elements drag although the subject is
        // read-only, and the only file written is the .adp's layout: block.

        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration, new DirectHistoryStack());
        var projectBefore = await File.ReadAllTextAsync(Path.Combine(_root, "App", "App.csproj"), TestContext.Current.CancellationToken);
        var solutionBefore = await File.ReadAllTextAsync(solution, TestContext.Current.CancellationToken);

        // Act.
        var error = await session.MoveElementToAsync("project:App/App.csproj", 120.5, 40, CancellationToken.None);

        // Assert.
        Assert.Equal("", error);
        var stored = RegistrationLayout.Read(registration);
        Assert.Equal(120.5, stored["project:App/App.csproj"].X);

        // Nothing the build owns was touched.
        Assert.Equal(projectBefore, await File.ReadAllTextAsync(Path.Combine(_root, "App", "App.csproj"), TestContext.Current.CancellationToken));
        Assert.Equal(solutionBefore, await File.ReadAllTextAsync(solution, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AStoredPosition_WinsForItsElement_WhileTheRestStayComputed()
    {
        // Requirement 6.3, element by element - so a solution that has gained a project keeps
        // every arrangement the user made rather than losing the lot because one id is new.

        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration, new DirectHistoryStack());
        var computed = Assert.IsType<DiagramAddDelta>(Assert.Single(session.Baseline()))
            .Elements.ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        // Act.
        await session.MoveElementToAsync("project:App/App.csproj", 999, 888, CancellationToken.None);
        var after = Assert.IsType<DiagramAddDelta>(Assert.Single(SessionFor(solution, registration).Baseline()))
            .Elements.ToDictionary(element => element.Id, element => (element.X, element.Y), StringComparer.Ordinal);

        // Assert.
        Assert.Equal((999, 888), after["project:App/App.csproj"]);
        Assert.Equal(computed["project:Library/Library.csproj"], after["project:Library/Library.csproj"]);
        Assert.Equal(computed["package:Serilog"], after["package:Serilog"]);
    }

    [Fact]
    public async Task ARefreshAfterTheSolutionGrows_KeepsTheArrangementOfWhatWasAlreadyThere()
    {
        // Requirement 7.1 and 7.2 together: the new project appears, and the refresh does not
        // cost the user the position they authored for an untouched one.

        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration, new DirectHistoryStack());
        session.Baseline();
        await session.MoveElementToAsync("project:App/App.csproj", 500, 400, CancellationToken.None);

        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act. A third project joins the solution, and the diagram is refreshed.
        Write("Extra/Extra.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(solution, """
                                               <Solution>
                                                 <Project Path="App/App.csproj" />
                                                 <Project Path="Library/Library.csproj" />
                                                 <Project Path="Extra/Extra.csproj" />
                                               </Solution>
                                               """, TestContext.Current.CancellationToken);
        session.Refresh();

        // Assert.
        var added = pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements).ToArray();
        Assert.Contains(added, element => element.Id == "project:Extra/Extra.csproj");
        var app = Assert.Single(SessionFor(solution, registration).Baseline().OfType<DiagramAddDelta>()
            .SelectMany(delta => delta.Elements), element => element.Id == "project:App/App.csproj");
        Assert.Equal(500, app.X);
        Assert.Equal(400, app.Y);
    }

    [Fact]
    public void ARemovedProject_DisappearsFromTheDiagram()
    {
        // Requirement 7.3.

        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration);
        session.Baseline();

        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act.
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="Library/Library.csproj" />
            </Solution>
            """);
        session.Refresh();

        // Assert.
        var removed = pushed.OfType<DiagramRemoveDelta>().SelectMany(delta => delta.ElementIds).ToArray();
        Assert.Contains("project:App/App.csproj", removed);
    }

    [Fact]
    public async Task AnEdge_CannotBeRepositioned_BecauseItHasNoPositionOfItsOwn()
    {
        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration, new DirectHistoryStack());

        // Act.
        var error = await session.MoveElementToAsync("depends:project:App/App.csproj->project:Library/Library.csproj", 1, 1, CancellationToken.None);

        // Assert.
        Assert.NotEqual("", error);
        Assert.Empty(RegistrationLayout.Read(registration));
    }

    [Fact]
    public async Task WithoutAHistory_TheDiagramIsReadOnly_AndSaysSo()
    {
        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration);

        // Act.
        var error = await session.MoveElementToAsync("project:App/App.csproj", 1, 1, CancellationToken.None);

        // Assert.
        Assert.Equal("This diagram is read-only.", error);
    }

    [Fact]
    public void AnUnreadableSolution_StillOpens_WithItsReasonCarried()
    {
        // Requirement 2.4: the failure is reported rather than presented as an empty diagram,
        // and above all the diagram opens.

        // Arrange.
        var solution = Write("Broken.slnx", "not xml at all <<<");
        var store = new DependencyGraphStore(new SolutionReader(), new ProjectReader(), new PackageDescriptionReader(Path.Combine("no", "such", "cache")));

        // Act.
        var deltas = new DotNetDependencyGraphSession(solution, store, new DependencyElementMapper()).Baseline();

        // Assert.
        Assert.Empty(deltas);
        Assert.NotEmpty(store.GetOrLoad(solution).Failures);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder a virus scanner still holds is not a test failure.
        }
    }


    // ---- a move reaches the connection that made it -----------------------------------------

    // THE DEFECT THESE GUARD. Every test above that checks a stored position does it by opening a
    // FRESH session and reading its baseline - which proves the registration was written and says
    // nothing about the session that is already open. That was the whole bug: the position was
    // written, nothing was pushed, and the element stayed where it was dropped from until a zoom
    // asked UpdateView for the diff. So each of these subscribes to the session that is open.

    [Fact]
    public async Task AMove_IsPushedToTheOpenSession_WithoutWaitingForAViewUpdate()
    {
        // Arrange.
        (string solution, string registration) = Seed();
        var session = SessionFor(solution, registration, new DirectHistoryStack());
        session.Baseline();
        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act.
        var error = await session.MoveElementToAsync("project:App/App.csproj", 321, 654, CancellationToken.None);

        // Assert. Pushed, by the move itself - no UpdateView, no Refresh, no zoom.
        Assert.Equal("", error);
        var moved = Assert.Single(
            pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == "project:App/App.csproj");
        Assert.Equal(321, moved.X);
        Assert.Equal(654, moved.Y);
    }

    [Fact]
    public async Task AnUndoneMove_IsPushedToo()
    {
        // The same gap had a second door: undo and redo rewrite the registration through the
        // project's history, never through this session, so they were invisible in exactly the
        // same way. The session listens to the history for that reason, and this is the proof.
        (string solution, string registration) = Seed();
        var history = new DirectHistoryStack();
        var session = SessionFor(solution, registration, history);
        session.Baseline();
        await session.MoveElementToAsync("project:App/App.csproj", 321, 654, CancellationToken.None);

        var pushed = new List<DiagramDelta>();
        session.Changed += (_, args) => pushed.AddRange(args.Deltas);

        // Act. What an undo's inverse does to the file, then what the real stack does after it.
        RegistrationLayout.SetPosition(registration, "project:App/App.csproj", new RegistrationPosition(10, 20));
        history.AnnounceChange();

        // Assert.
        var restored = Assert.Single(
            pushed.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements),
            element => element.Id == "project:App/App.csproj");
        Assert.Equal(10, restored.X);
        Assert.Equal(20, restored.Y);
    }

    [Fact]
    public async Task AClosedSession_StopsListeningToTheProjectsHistory()
    {
        // The history is the PROJECT's and outlives any one diagram. A session that never let go
        // would keep a closed diagram alive, re-rendering on every command anybody runs.
        (string solution, string registration) = Seed();
        var history = new DirectHistoryStack();
        var session = SessionFor(solution, registration, history);
        session.Baseline();
        var pushed = 0;
        session.Changed += (_, _) => pushed++;

        await session.DisposeAsync();
        RegistrationLayout.SetPosition(registration, "project:App/App.csproj", new RegistrationPosition(77, 88));
        history.AnnounceChange();

        Assert.Equal(0, pushed);
    }
}
