using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Context.Tests;

public class ContextSelectionStoreTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Rooted on the current platform ("\root" here, "/root" there), not a Windows drive
    // literal: Relocate walks the path with the platform's own separators, and a "C:\..."
    // literal dissolved into nothing on the Linux CI runner.
    private static readonly string Root = System.IO.Path.DirectorySeparatorChar + "root";

    private readonly ContextSelectionStore _store = new(idleTimeout: TimeSpan.FromMinutes(5));

    public void Dispose() => _store.Dispose();

    private static ContextRediscovery NoRediscovery => (record, _) => ValueTask.FromResult(record);

    private static ContextSelectionRecord Record(ContextSelectionStoreStubResolver resolver, params string[] path)
    {
        var id = new ContextSource { EntryId = ShortGuid.NewShortGuid() };
        var chain = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = id, Path = new Path() };
        chain.Path.Segments.AddRange(path);
        var level = new ContextResolvedLevel(
            id, path, ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, System.IO.Path.Combine([Root, .. path]), false, (ShortGuid)id.EntryId),
            new ContextLevelDetail { Entry = new EntryDetail { Kind = EntryKind.File, Available = true } },
            resolver);
        return new ContextSelectionRecord(chain, [level], [], null, []);
    }

    private static async Task<ContextMessage> ReadAsync(ChannelReader<ContextMessage> reader)
    {
        // Linked to the test's own token, so a read that would otherwise sit here for the whole
        // timeout gives up as soon as the test itself is cancelled.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        return await reader.ReadAsync(cts.Token);
    }

    /// <summary>
    /// Registration writes three messages: the selection baseline (or the root actions when
    /// nothing is selected), the project's actions - undo and redo (diagram-undo-redo
    /// Deviation 1) - and the project's problems (errors-and-warnings-panel Requirement 1.2).
    /// A test that goes on to assert the next message reads past all three here.
    /// </summary>
    private static async Task TestBaselineAsync(ChannelReader<ContextMessage> reader)
    {
        var selection = await ReadAsync(reader);
        var projectActions = await ReadAsync(reader);
        var problems = await ReadAsync(reader);
        Assert.NotNull(selection);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, projectActions.MessageCase);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, problems.MessageCase);
    }

    private static ContextActionGroupDefinition RootGroup() =>
        new([new ContextActionDefinition("hierarchy.add", "Add…", "mdi-plus")]);

    private static ContextActionGroupDefinition ProjectGroup() =>
        new([new ContextActionDefinition("history.undo", "Undo", "mdi-undo")]);

    [Fact]
    public async Task Register_WithNothingSelected_CarriesTheRootActionsOnTheBaseline()
    {
        // Arrange.
        // The explorer's empty space needs a menu without asking; nothing is selected, so the
        // selection stays absent and only the actions travel.
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [RootGroup()], [], new ProjectProblems());

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        var action = Assert.Single(Assert.Single(message.Selection.Actions).Actions);
        Assert.Equal("hierarchy.add", action.Id);
    }

    [Fact]
    public async Task Clear_CarriesTheRootActions_WhileASetCarriesTheSelectionsOwn()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [RootGroup()], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);

        // Arrange, continued.
        _store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);
        var selected = await ReadAsync(channel.Reader);

        // Act.
        _store.Clear(watchId);
        var cleared = await ReadAsync(channel.Reader);

        // Assert.
        // A selection's message carries that selection's actions (none, for this record) - not the root's.
        Assert.NotNull(selected.Selection.Selection);
        Assert.Empty(selected.Selection.Actions);
        // Back to nothing selected: the root's actions are back too.
        Assert.Null(cleared.Selection.Selection);
        Assert.Equal("hierarchy.add", Assert.Single(Assert.Single(cleared.Selection.Actions).Actions).Id);
    }

    [Fact]
    public async Task Register_WithNothingSelected_WritesAnEmptyBaseline()
    {
        // Arrange.
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [], [], new ProjectProblems());

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.False(message.Selection.Transient);
    }

    [Fact]
    public async Task Register_AfterASet_WritesTheCurrentSelectionAsBaseline()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        _store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Equal(new[] { "a.txt" }, message.Selection.Selection.Path.Segments);
    }

    [Fact]
    public async Task Set_PushesToTheRegisteredWriterOnly()
    {
        // Arrange.
        var mine = Channel.CreateUnbounded<ContextMessage>();
        var theirs = Channel.CreateUnbounded<ContextMessage>();
        var myId = ShortGuid.NewShortGuid();
        var theirId = ShortGuid.NewShortGuid();
        _store.Register(myId, Root, mine.Writer, [], [], new ProjectProblems());
        _store.Register(theirId, Root, theirs.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(mine.Reader);
        await TestBaselineAsync(theirs.Reader);

        _store.Set(myId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);

        // Act and assert, step by step.
        var message = await ReadAsync(mine.Reader);
        Assert.Equal(new[] { "a.txt" }, message.Selection.Selection.Path.Segments);
        Assert.False(theirs.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PushTransient_PushesTransientAndLeavesGetUnchanged()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);
        var current = Record(new ContextSelectionStoreStubResolver(), "current.txt");
        _store.Set(watchId, Root, current, NoRediscovery);
        await ReadAsync(channel.Reader);

        _store.PushTransient(watchId, Record(new ContextSelectionStoreStubResolver(), "preview.txt"));

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.True(message.Selection.Transient);
        Assert.Equal(new[] { "preview.txt" }, message.Selection.Selection.Path.Segments);
        Assert.Equal(new[] { "current.txt" }, _store.Get(watchId)!.Chain.Path.Segments);
    }

    [Fact]
    public async Task Clear_PushesAnEmptySelectionAndDisposesTracks()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);
        var resolver = new ContextSelectionStoreStubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);
        await ReadAsync(channel.Reader);

        _store.Clear(watchId);

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.Null(_store.Get(watchId));
        Assert.Equal(1, resolver.Disposed);
    }

    [Fact]
    public async Task UpdateFromTrack_WithAPath_RewritesTheSelectionAndRediscovers()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);
        var resolver = new ContextSelectionStoreStubResolver();
        var rediscovered = 0;
        _store.Set(watchId, Root, Record(resolver, "a.txt"), (record, _) =>
        {
            rediscovered++;
            return ValueTask.FromResult(record with
            {
                Actions = [new ContextActionGroupDefinition([new ContextActionDefinition("rename", "Rename", "")])],
            });
        });
        await ReadAsync(channel.Reader);

        resolver.Fire(["b.txt"]);

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Equal(new[] { "b.txt" }, message.Selection.Selection.Path.Segments);
        Assert.Equal("rename", message.Selection.Actions.Single().Actions.Single().Id);
        Assert.Equal(1, rediscovered);
        Assert.Equal(new[] { "b.txt" }, _store.Get(watchId)!.Innermost.RelativePath);
    }

    [Fact]
    public async Task UpdateFromTrack_MovesTheTargetAlongWithThePath()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var resolver = new ContextSelectionStoreStubResolver();
        ContextSelectionRecord? rediscoveredWith = null;
        _store.Set(watchId, Root, Record(resolver, "docs", "a.txt"), (record, _) =>
        {
            rediscoveredWith = record;
            return ValueTask.FromResult(record);
        });

        // Arrange, continued.
        resolver.Fire(["documents", "b.txt"]);

        // Act.
        var deadline = DateTime.UtcNow + Timeout;
        while (rediscoveredWith is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Assert.
        Assert.Equal(System.IO.Path.Combine(Root, "documents", "b.txt"), rediscoveredWith!.Innermost.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task UpdateFromTrack_OnAnElementLevel_LeavesTheBodyPathAlone()
    {
        // Arrange. An element level's relative path is DISPLAY text - a mindmap node's label, a
        // causal-loop variable's name - while its target's ResolvedFullPath is the body file
        // that holds it. Relocating the file path from display segments turned the target into
        // "<folder>/<new label>", after which every re-resolution and property describe on the
        // selection read a file that does not exist. Found in the field: committing a value in
        // the property grid made the selection go away.
        var watchId = ShortGuid.NewShortGuid();
        var fileResolver = new ContextSelectionStoreStubResolver();
        var elementResolver = new ContextSelectionStoreStubResolver();
        var bodyPath = System.IO.Path.Combine(Root, "plan.cld");

        var fileId = new ContextSource { EntryId = ShortGuid.NewShortGuid() };
        var fileChain = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = fileId, Path = new Path() };
        fileChain.Path.Segments.Add("plan.cld");
        var elementId = new ContextSource { ElementId = new ElementId { Value = "variable:incidents" } };
        var elementChain = new ContextSelection { Source = ContextSelectionSource.DiagramCanvas, Id = elementId, Path = new Path() };
        elementChain.Path.Segments.Add("Incidents");
        fileChain.Child = elementChain;

        var fileLevel = new ContextResolvedLevel(
            fileId, ["plan.cld"], ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, bodyPath, false, (ShortGuid)fileId.EntryId),
            new ContextLevelDetail { Entry = new EntryDetail { Kind = EntryKind.File, Available = true } },
            fileResolver);
        var elementLevel = new ContextResolvedLevel(
            elementId, ["Incidents"], ContextScope.DiagramElement,
            new ContextTarget(ContextScope.DiagramElement, bodyPath, false, default, Root, watchId, "variable:incidents"),
            new ContextLevelDetail { Element = new ElementDetail { Text = "Incidents" } },
            elementResolver);

        ContextSelectionRecord? rediscoveredWith = null;
        _store.Set(watchId, Root, new ContextSelectionRecord(fileChain, [fileLevel, elementLevel], [], null, []), (record, _) =>
        {
            rediscoveredWith = record;
            return ValueTask.FromResult(record);
        });

        // Act: the element's label changed, so its track reports the new display path.
        elementResolver.Fire(["New label"]);

        var deadline = DateTime.UtcNow + Timeout;
        while (rediscoveredWith is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Assert: the display path moved, the body file did not.
        Assert.NotNull(rediscoveredWith);
        Assert.Equal(new[] { "New label" }, rediscoveredWith!.Innermost.RelativePath);
        Assert.Equal(bodyPath, rediscoveredWith.Innermost.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task UpdateFromTrack_WithNull_Clears()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);
        var resolver = new ContextSelectionStoreStubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);
        await ReadAsync(channel.Reader);

        resolver.Fire(null);

        // Act and assert, step by step.
        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.Null(_store.Get(watchId));
    }

    [Fact]
    public async Task IdleEviction_RemovesAnEntryNoStreamEverClaimed()
    {
        // Arrange.
        using var store = new ContextSelectionStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);

        // Act.
        var deadline = DateTime.UtcNow + Timeout;
        while (store.Get(watchId) is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Assert.
        Assert.Null(store.Get(watchId));
    }

    [Fact]
    public async Task Register_CancelsIdleEviction()
    {
        // Arrange.
        using var store = new ContextSelectionStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);
        store.Register(watchId, Root, Channel.CreateUnbounded<ContextMessage>().Writer, [], [], new ProjectProblems());

        // Act.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(store.Get(watchId));
    }

    [Fact]
    public async Task Remove_DisposesTracksAndForgetsTheSelection()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        _store.Register(watchId, Root, Channel.CreateUnbounded<ContextMessage>().Writer, [], [], new ProjectProblems());
        var resolver = new ContextSelectionStoreStubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);

        // Act.
        _store.Remove(watchId);

        // Assert.
        Assert.Equal(1, resolver.Disposed);
        Assert.Null(_store.Get(watchId));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Register_Twice_SupersedesTheFirstWriter()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var first = Channel.CreateUnbounded<ContextMessage>();
        var second = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, first.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(first.Reader);
        _store.Register(watchId, Root, second.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(second.Reader);

        _store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);

        // Act and assert, step by step.
        await ReadAsync(second.Reader);
        Assert.False(first.Reader.TryRead(out _));
    }

    // ---- the project's own actions -----------------------------------------------------

    [Fact]
    public async Task Register_CarriesTheProjectActionsOnTheirOwnMessageAfterTheBaseline()
    {
        // Arrange.
        // Undo and redo belong to the project, not the selection, so they travel on their own
        // message right after the selection baseline (diagram-undo-redo Deviation 1).
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [], [ProjectGroup()], new ProjectProblems());

        // Act and assert, step by step.
        var selection = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Selection, selection.MessageCase);
        var projectActions = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, projectActions.MessageCase);
        Assert.Equal("history.undo", Assert.Single(Assert.Single(projectActions.ProjectActions.Actions).Actions).Id);
    }

    [Fact]
    public async Task PushProjectActions_ReachesOnlyConnectionsInThatProject()
    {
        // Arrange.
        const string other = @"C:\other";
        var mine = Channel.CreateUnbounded<ContextMessage>();
        var theirs = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(ShortGuid.NewShortGuid(), Root, mine.Writer, [], [ProjectGroup()], new ProjectProblems());
        _store.Register(ShortGuid.NewShortGuid(), other, theirs.Writer, [], [ProjectGroup()], new ProjectProblems());
        await TestBaselineAsync(mine.Reader);
        await TestBaselineAsync(theirs.Reader);

        _store.PushProjectActions(Root, [ProjectGroup()]);

        // Act and assert, step by step.
        var message = await ReadAsync(mine.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, message.MessageCase);
        Assert.False(theirs.Reader.TryRead(out _), "A project-actions push reached a connection in another project.");
    }

    // ---- the project's problems --------------------------------------------------------

    [Fact]
    public async Task Register_CarriesTheProblemsOnTheBaseline()
    {
        // Arrange.
        // The panel is current the moment it connects (errors-and-warnings-panel
        // Requirement 1.2): the baseline's third message is the project's problems.
        var channel = Channel.CreateUnbounded<ContextMessage>();
        var problems = new ProjectProblems { State = ProblemSetState.Validated, ErrorCount = 2, WarningCount = 1 };

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [], [], problems);

        // Act and assert, step by step.
        await ReadAsync(channel.Reader); // the selection baseline
        await ReadAsync(channel.Reader); // the project actions
        var message = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, message.MessageCase);
        Assert.Same(problems, message.Problems);
    }

    [Fact]
    public async Task PushProblems_ReachesEveryConnectionInTheProject()
    {
        // Arrange.
        // Two viewers of one project cannot disagree about what is wrong
        // (errors-and-warnings-panel Requirement 6.4).
        var first = Channel.CreateUnbounded<ContextMessage>();
        var second = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(ShortGuid.NewShortGuid(), Root, first.Writer, [], [], new ProjectProblems());
        _store.Register(ShortGuid.NewShortGuid(), Root, second.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(first.Reader);
        await TestBaselineAsync(second.Reader);

        var problems = new ProjectProblems { State = ProblemSetState.Validated, ErrorCount = 1 };
        _store.PushProblems(Root, problems);

        // Act and assert, step by step.
        var toFirst = await ReadAsync(first.Reader);
        var toSecond = await ReadAsync(second.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, toFirst.MessageCase);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, toSecond.MessageCase);
        Assert.Same(toFirst.Problems, toSecond.Problems);
    }

    [Fact]
    public async Task PushProblems_ReachesNoConnectionInAnotherProject()
    {
        // Arrange.
        const string other = @"C:\other";
        var mine = Channel.CreateUnbounded<ContextMessage>();
        var theirs = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(ShortGuid.NewShortGuid(), Root, mine.Writer, [], [], new ProjectProblems());
        _store.Register(ShortGuid.NewShortGuid(), other, theirs.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(mine.Reader);
        await TestBaselineAsync(theirs.Reader);

        _store.PushProblems(Root, new ProjectProblems());

        // Act and assert, step by step.
        var message = await ReadAsync(mine.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, message.MessageCase);
        Assert.False(theirs.Reader.TryRead(out _), "A problems push reached a connection in another project.");
    }

    [Fact]
    public async Task PushProblems_NeverDisturbsASelection()
    {
        // Arrange.
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], [], new ProjectProblems());
        await TestBaselineAsync(channel.Reader);
        _store.Set(watchId, Root, Record(new ContextSelectionStoreStubResolver(), "a.txt"), NoRediscovery);
        await ReadAsync(channel.Reader);
        var before = _store.Get(watchId);

        // Act.
        _store.PushProblems(Root, new ProjectProblems { State = ProblemSetState.Validated });

        // Assert.
        Assert.Same(before, _store.Get(watchId));
        var message = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Problems, message.MessageCase);
    }

}
