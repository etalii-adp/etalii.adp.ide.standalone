using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// One knowledge file open for editing as the host opens it: a session whose edits are written
/// through a real history, with the module's own handlers behind it. What the session pushes is
/// kept, so a test can wait for what became of an edit and look at what was shown meanwhile.
/// </summary>
internal sealed class EditingTable : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private readonly Dictionary<ShortGuid, TaskCompletionSource<TableEditSettled>> _settled = [];

    /// <summary>While set, a write waits for it: the time between an edit being shown and being written, held open.</summary>
    private TaskCompletionSource? _hold;

    public EditingTable(string path)
    {
        Path = path;
        var documents = new KnowledgeDocuments();
        var services = new Handlers
        {
            [typeof(ICommandHandler<KnowledgeEditCommand>)] = new KnowledgeEditCommandHandler(documents),
            [typeof(ICommandHandler<RestoreDocumentCommand<IKnowledgeDocumentStore>>)] = new RestoreDocumentCommandHandler<IKnowledgeDocumentStore>(documents),
        };
        History = new HistoryStack(new CommandDispatcher(services));
        Session = new KnowledgeSession(path, KnowledgeDocumentStore.Read, Write, documents);
        Session.Changed += (_, args) =>
        {
            lock (_gate)
            {
                foreach (var settled in args.Changes.OfType<TableEditSettled>())
                {
                    Waiter(settled.EditId).TrySetResult(settled);
                }
            }
        };
    }

    public string Path { get; }

    public HistoryStack History { get; }

    public KnowledgeSession Session { get; }

    /// <summary>What a write answers instead of being made, when a test wants one refused.</summary>
    public Func<KnowledgeEditCommand, CommandResult?>? Instead { get; set; }

    /// <summary>The file as it is on disk now, read as the module reads it.</summary>
    public KnowledgeTable OnDisk() => KnowledgeDocumentStore.Read(Path).Body!.Table;

    public byte[] Bytes() => File.ReadAllBytes(Path);

    /// <summary>The table's lines as the session shows them now, all of them.</summary>
    public IReadOnlyList<TableRow> Lines()
    {
        TableRowsChanged? rows = null;
        void Keep(object? sender, TableChangedEventArgs args) => rows = args.Changes.OfType<TableRowsChanged>().LastOrDefault() ?? rows;
        Session.Changed += Keep;
        Session.SetWindow(0, int.MaxValue);
        Session.Changed -= Keep;
        return rows?.Rows ?? [];
    }

    /// <summary>Makes writes wait until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        TaskCompletionSource? hold;
        lock (_gate)
        {
            hold = _hold;
            _hold = null;
        }

        hold?.TrySetResult();
    }

    /// <summary>Makes a gesture that is expected to be accepted, and waits for what became of its write.</summary>
    public async Task<TableEditSettled> Edit(TableGesture gesture)
    {
        (ShortGuid id, string answer) = Begin(gesture);
        Assert.Equal("", answer);
        var settled = await Settled(id);
        Assert.True(settled.Written, $"{gesture.Kind} was not written: {settled.Error}");
        return settled;
    }

    /// <summary>Makes a gesture and answers at once, as the service does: the edit's id and the session's answer.</summary>
    public (ShortGuid EditId, string Answer) Begin(TableGesture gesture)
    {
        var id = ShortGuid.NewShortGuid();
        return (id, Session.Edit(id, gesture));
    }

    public Task<TableEditSettled> Settled(ShortGuid editId)
    {
        lock (_gate)
        {
            return Waiter(editId).Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return Session.DisposeAsync();
    }

    private TaskCompletionSource<TableEditSettled> Waiter(ShortGuid editId)
    {
        if (!_settled.TryGetValue(editId, out var waiter))
        {
            waiter = new TaskCompletionSource<TableEditSettled>(TaskCreationOptions.RunContinuationsAsynchronously);
            _settled[editId] = waiter;
        }

        return waiter;
    }

    private async Task<CommandResult> Write(KnowledgeEditCommand command)
    {
        Task? hold;
        lock (_gate)
        {
            hold = _hold?.Task;
        }

        if (hold is not null)
        {
            await hold.WaitAsync(Patience);
        }

        return Instead?.Invoke(command) ?? await History.ExecuteAsync(command);
    }

    /// <summary>The handlers by the type the dispatcher asks for them under.</summary>
    private sealed class Handlers : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
