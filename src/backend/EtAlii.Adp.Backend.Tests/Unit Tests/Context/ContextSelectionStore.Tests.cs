using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Contracts;
using Xunit;
using ShortGuid = EtAlii.Adp.ShortGuid;

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
        using var cts = new CancellationTokenSource(Timeout);
        return await reader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task Register_WithNothingSelected_WritesAnEmptyBaseline()
    {
        var channel = Channel.CreateUnbounded<ContextMessage>();

        _store.Register(ShortGuid.NewShortGuid(), channel.Writer);

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

        _store.Register(watchId, channel.Writer);

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
        _store.Register(myId, mine.Writer);
        _store.Register(theirId, theirs.Writer);
        await ReadAsync(mine.Reader);
        await ReadAsync(theirs.Reader);

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
        _store.Register(watchId, channel.Writer);
        await ReadAsync(channel.Reader);
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
        _store.Register(watchId, channel.Writer);
        await ReadAsync(channel.Reader);
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
        _store.Register(watchId, channel.Writer);
        await ReadAsync(channel.Reader);
        var resolver = new StubResolver();
        var rediscovered = 0;
        _store.Set(watchId, Root, Record(resolver, "a.txt"), (record, _) =>
        {
            rediscovered++;
            return ValueTask.FromResult(record with
            {
                Actions = [new ContextActionGroupDefinition([new ContextActionDefinition("rename", "Rename", "", null, true, "", null)])],
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
            await Task.Delay(10);
        }

        Assert.Equal(System.IO.Path.Combine(Root, "documents", "b.txt"), rediscoveredWith!.Innermost.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task UpdateFromTrack_WithNull_Clears()
    {
        var watchId = ShortGuid.NewShortGuid();
        var channel = Channel.CreateUnbounded<ContextMessage>();
        _store.Register(watchId, channel.Writer);
        await ReadAsync(channel.Reader);
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
            await Task.Delay(10);
        }

        Assert.Null(store.Get(watchId));
    }

    [Fact]
    public async Task Register_CancelsIdleEviction()
    {
        using var store = new ContextSelectionStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);
        store.Register(watchId, Channel.CreateUnbounded<ContextMessage>().Writer);

        await Task.Delay(200);

        Assert.NotNull(store.Get(watchId));
    }

    [Fact]
    public async Task Remove_DisposesTracksAndForgetsTheSelection()
    {
        var watchId = ShortGuid.NewShortGuid();
        _store.Register(watchId, Channel.CreateUnbounded<ContextMessage>().Writer);
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
        _store.Register(watchId, first.Writer);
        await ReadAsync(first.Reader);
        _store.Register(watchId, second.Writer);
        await ReadAsync(second.Reader);

        _store.Set(watchId, Root, Record(new StubResolver(), "a.txt"), NoRediscovery);

        await ReadAsync(second.Reader);
        Assert.False(first.Reader.TryRead(out _));
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
