using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;

using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class ContextSelectionStoreTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const string Root = @"C:\root";

    private readonly ContextSelectionStore _store = new(idleTimeout: TimeSpan.FromMinutes(5));

    public void Dispose() => _store.Dispose();

    private static ContextRediscovery NoRediscovery => (record, _) => ValueTask.FromResult(record);

    private static ContextSelectionRecord Record(StubResolver resolver, params string[] path)
    {
        var id = new ContextSource { EntryId = ShortGuid.NewShortGuid() };
        var chain = new ContextSelection { Source = ContextSelectionSource.Explorer, Id = id, Path = new Path() };
        chain.Path.Segments.AddRange(path);
        var level = new ContextResolvedLevel(
            ContextSelectionSource.Explorer, id, path, ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, System.IO.Path.Combine(Root, string.Join('\\', path)), false, (ShortGuid)id.EntryId),
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
    /// Registration writes two messages: the selection baseline (or the root actions when
    /// nothing is selected) and, on its own, the project's actions - undo and redo
    /// (diagram-undo-redo Deviation 1). A test that goes on to assert the next message reads
    /// past both here, and gets the selection baseline back.
    /// </summary>
    private static async Task TestBaselineAsync(ChannelReader<ContextMessage> reader)
    {
        var selection = await ReadAsync(reader);
        var projectActions = await ReadAsync(reader);
        Assert.NotNull(selection);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, projectActions.MessageCase);
    }

    private static ContextActionGroupDefinition RootGroup() =>
        new([new ContextActionDefinition("hierarchy.add", "Add…", "mdi-plus")]);

    private static ContextActionGroupDefinition ProjectGroup() =>
        new([new ContextActionDefinition("history.undo", "Undo", "mdi-undo")]);

    [Fact]
    public async Task Register_WithNothingSelected_CarriesTheRootActionsOnTheBaseline()
    {
        // The explorer's empty space needs a menu without asking; nothing is selected, so the
        // selection stays absent and only the actions travel.
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [RootGroup()], []);

        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        var action = Assert.Single(Assert.Single(message.Selection.Actions).Actions);
        Assert.Equal("hierarchy.add", action.Id);
    }

    [Fact]
    public async Task Clear_CarriesTheRootActions_WhileASetCarriesTheSelectionsOwn()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [RootGroup()], []);
        await TestBaselineAsync(channel.Reader);

        _store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);
        var selected = await ReadAsync(channel.Reader);

        _store.Clear(watchId);
        var cleared = await ReadAsync(channel.Reader);

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
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [], []);

        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.False(message.Selection.Transient);
    }

    [Fact]
    public async Task Register_AfterASet_WritesTheCurrentSelectionAsBaseline()
    {
        var watchId = ShortGuid.NewShortGuid();
        _store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(watchId, Root, channel.Writer, [], []);

        var message = await ReadAsync(channel.Reader);
        Assert.Equal(new[] { "a.txt" }, message.Selection.Selection.Path.Segments);
    }

    [Fact]
    public async Task Set_PushesToTheRegisteredWriterOnly()
    {
        var mine = Channel.CreateUnbounded<ContextMessage>();
        var theirs = Channel.CreateUnbounded<ContextMessage>();
        var myId = ShortGuid.NewShortGuid();
        var theirId = ShortGuid.NewShortGuid();
        _store.Register(myId, Root, mine.Writer, [], []);
        _store.Register(theirId, Root, theirs.Writer, [], []);
        await TestBaselineAsync(mine.Reader);
        await TestBaselineAsync(theirs.Reader);

        _store.Set(myId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);

        var message = await ReadAsync(mine.Reader);
        Assert.Equal(new[] { "a.txt" }, message.Selection.Selection.Path.Segments);
        Assert.False(theirs.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PushTransient_PushesTransientAndLeavesGetUnchanged()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], []);
        await TestBaselineAsync(channel.Reader);
        var current = Record(new StubResolver(), "current.txt");
        _store.Set(watchId, Root, current, NoRediscovery);
        await ReadAsync(channel.Reader);

        _store.PushTransient(watchId, Record(new StubResolver(), "preview.txt"));

        var message = await ReadAsync(channel.Reader);
        Assert.True(message.Selection.Transient);
        Assert.Equal(new[] { "preview.txt" }, message.Selection.Selection.Path.Segments);
        Assert.Equal(new[] { "current.txt" }, _store.Get(watchId)!.Chain.Path.Segments);
    }

    [Fact]
    public async Task Clear_PushesAnEmptySelectionAndDisposesTracks()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], []);
        await TestBaselineAsync(channel.Reader);
        var resolver = new StubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);
        await ReadAsync(channel.Reader);

        _store.Clear(watchId);

        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.Null(_store.Get(watchId));
        Assert.Equal(1, resolver.Disposed);
    }

    [Fact]
    public async Task UpdateFromTrack_WithAPath_RewritesTheSelectionAndRediscovers()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], []);
        await TestBaselineAsync(channel.Reader);
        var resolver = new StubResolver();
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

        var message = await ReadAsync(channel.Reader);
        Assert.Equal(new[] { "b.txt" }, message.Selection.Selection.Path.Segments);
        Assert.Equal("rename", message.Selection.Actions.Single().Actions.Single().Id);
        Assert.Equal(1, rediscovered);
        Assert.Equal(new[] { "b.txt" }, _store.Get(watchId)!.Innermost.RelativePath);
    }

    [Fact]
    public async Task UpdateFromTrack_MovesTheTargetAlongWithThePath()
    {
        var watchId = ShortGuid.NewShortGuid();
        var resolver = new StubResolver();
        ContextSelectionRecord? rediscoveredWith = null;
        _store.Set(watchId, Root, Record(resolver, "docs", "a.txt"), (record, _) =>
        {
            rediscoveredWith = record;
            return ValueTask.FromResult(record);
        });

        resolver.Fire(["documents", "b.txt"]);

        var deadline = DateTime.UtcNow + Timeout;
        while (rediscoveredWith is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(System.IO.Path.Combine(Root, "documents", "b.txt"), rediscoveredWith!.Innermost.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task UpdateFromTrack_WithNull_Clears()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, channel.Writer, [], []);
        await TestBaselineAsync(channel.Reader);
        var resolver = new StubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);
        await ReadAsync(channel.Reader);

        resolver.Fire(null);

        var message = await ReadAsync(channel.Reader);
        Assert.Null(message.Selection.Selection);
        Assert.Null(_store.Get(watchId));
    }

    [Fact]
    public async Task IdleEviction_RemovesAnEntryNoStreamEverClaimed()
    {
        using var store = new ContextSelectionStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);

        var deadline = DateTime.UtcNow + Timeout;
        while (store.Get(watchId) is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Null(store.Get(watchId));
    }

    [Fact]
    public async Task Register_CancelsIdleEviction()
    {
        using var store = new ContextSelectionStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);
        store.Register(watchId, Root, Channel.CreateUnbounded<ContextMessage>().Writer, [], []);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.NotNull(store.Get(watchId));
    }

    [Fact]
    public async Task Remove_DisposesTracksAndForgetsTheSelection()
    {
        var watchId = ShortGuid.NewShortGuid();
        _store.Register(watchId, Root, Channel.CreateUnbounded<ContextMessage>().Writer, [], []);
        var resolver = new StubResolver();
        _store.Set(watchId, Root, Record(resolver, "a.txt"), NoRediscovery);

        _store.Remove(watchId);

        Assert.Equal(1, resolver.Disposed);
        Assert.Null(_store.Get(watchId));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Register_Twice_SupersedesTheFirstWriter()
    {
        var watchId = ShortGuid.NewShortGuid();
        var first = Channel.CreateUnbounded<ContextMessage>();
        var second = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, Root, first.Writer, [], []);
        await TestBaselineAsync(first.Reader);
        _store.Register(watchId, Root, second.Writer, [], []);
        await TestBaselineAsync(second.Reader);

        _store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);

        await ReadAsync(second.Reader);
        Assert.False(first.Reader.TryRead(out _));
    }

    // ---- the project's own actions -----------------------------------------------------

    [Fact]
    public async Task Register_CarriesTheProjectActionsOnTheirOwnMessageAfterTheBaseline()
    {
        // Undo and redo belong to the project, not the selection, so they travel on their own
        // message right after the selection baseline (diagram-undo-redo Deviation 1).
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), Root, channel.Writer, [], [ProjectGroup()]);

        var selection = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.Selection, selection.MessageCase);
        var projectActions = await ReadAsync(channel.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, projectActions.MessageCase);
        Assert.Equal("history.undo", Assert.Single(Assert.Single(projectActions.ProjectActions.Actions).Actions).Id);
    }

    [Fact]
    public async Task PushProjectActions_ReachesOnlyConnectionsInThatProject()
    {
        const string other = @"C:\other";
        var mine = Channel.CreateUnbounded<ContextMessage>();
        var theirs = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(ShortGuid.NewShortGuid(), Root, mine.Writer, [], [ProjectGroup()]);
        _store.Register(ShortGuid.NewShortGuid(), other, theirs.Writer, [], [ProjectGroup()]);
        await TestBaselineAsync(mine.Reader);
        await TestBaselineAsync(theirs.Reader);

        _store.PushProjectActions(Root, [ProjectGroup()]);

        var message = await ReadAsync(mine.Reader);
        Assert.Equal(ContextMessage.MessageOneofCase.ProjectActions, message.MessageCase);
        Assert.False(theirs.Reader.TryRead(out _), "A project-actions push reached a connection in another project.");
    }

    /// <summary>A resolver whose tracks the test can fire by hand and count disposals of.</summary>
    private sealed class StubResolver : IContextSourceResolver
    {
        private Action<IReadOnlyList<string>?>? _onChange;

        public int Disposed { get; private set; }

        public void Fire(IReadOnlyList<string>? path) => _onChange?.Invoke(path);

        public bool CanResolve(ContextSource source) => true;

        public ValueTask<ContextLevelResolution> ResolveAsync(
            ShortGuid watchId, string rootPath, ContextSelectionSource source, ContextSource id,
            IReadOnlyList<string> clientPath, ContextResolvedLevel? parent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

        public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
        {
            _onChange = onChange;
            return new Subscription(this);
        }

        private sealed class Subscription(StubResolver owner) : IDisposable
        {
            public void Dispose() => owner.Disposed++;
        }
    }
}
