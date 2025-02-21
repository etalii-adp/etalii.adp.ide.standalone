namespace EtAlii.Adp.Client;

public class HistoryManager
{
    public int Future => _future.Count;
    public int History => _history.Count;
    
    private readonly Stack<Change> _history = new();
    private readonly Stack<Change> _future = new();

    private readonly ChangePusher _changePusher;

    private readonly ILogger _logger;

    public event Action Changed = null!;
    
    public HistoryManager(ILoggerFactory loggerFactory, ChangePusher changePusher)
    {
        _changePusher = changePusher;
        _logger = loggerFactory.CreateLogger<UserManager>();
    }

    public async Task Push(Change change)
    {
        await _changePusher.Enqueue(change);
        
        _history.Push(change);
        _future.Clear();
        Changed.Invoke();
    }

    public async Task Push(Change[] changes)
    {
        await _changePusher.Enqueue(changes);

        foreach (var change in changes)
        {
            _history.Push(change);
        }
        
        _future.Clear();
        Changed.Invoke();
    }

    public async Task<bool> TryUndo()
    {
        var success = _history.TryPop(out var change);
        if (!success || change is null) return success;
        
        change.Undo = true;
        _future.Push(change);
        await _changePusher.Enqueue(change);
        Changed.Invoke();
        return success;
    }
    
    public async Task<bool> TryRedo()
    {
        var success = _future.TryPop(out var change);
        if (!success || change is null) return success;
        
        change.Undo = false;
        _history.Push(change);
        await _changePusher.Enqueue(change);
        Changed.Invoke();
        return success;
    }
}