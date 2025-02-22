namespace EtAlii.Adp.Client;

public class HistoryManager
{
    public int Future => _future.Count;
    public int History => _history.Count;
    
    private readonly Stack<Memento> _history = new();
    private readonly Stack<Memento> _future = new();

    private readonly ChangePusher _changePusher;

    private readonly ILogger _logger;

    public event Action Changed = null!;
    
    public HistoryManager(
        ILoggerFactory loggerFactory, 
        ChangePusher changePusher)
    {
        _changePusher = changePusher;
        _logger = loggerFactory.CreateLogger<UserManager>();
    }

    public async Task Push(Command command, DiagramContext context)
    {
        _logger.LogInformation("Pushing command on history {CommandName}", command.GetType().Name);
        
        var handler = context.CommandHandlers.Single(h => h.CanHandle(command));
        await handler.Execute(command, context);

        if (handler.SendToBackend)
        {
            await _changePusher.Enqueue(command);
        }
        if (handler.UseInUndoRedo)
        {
            _history.Push(new Memento { Commands = [ command ] });
            _future.Clear();
        }
        Changed.Invoke();
    }

    public async Task Push(Command[] commands, DiagramContext context)
    {
        var mappings = commands
            .Select(c => new { Command = c, Handler = context.CommandHandlers.Single(h => h.CanHandle(c)) })
            .ToArray();

        var commandsToPush = mappings
            .Where(m => m.Handler.SendToBackend)
            .Select(m => m.Command)
            .ToArray();

        var commandsToRemember = mappings
            .Where(m => m.Handler.UseInUndoRedo)
            .Select(m => m.Command)
            .ToArray();
        
        foreach (var mapping in mappings)
        {
            await mapping.Handler.Execute(mapping.Command, context);
        }

        if (commandsToPush.Any())
        {
            await _changePusher.Enqueue(commandsToPush);
        }

        if (commandsToRemember.Any())
        {
            _history.Push(new Memento { Commands = commandsToRemember });
            _future.Clear();
        }
        
        Changed.Invoke();
    }

    public async Task<bool> TryUndo(DiagramContext context)
    {
        _logger.LogInformation("Undoing history");

        var success = _history.TryPop(out var memento);
        if (!success || memento is null) return success;

        var commands = memento.Commands
            .Reverse()
            .ToArray();
        foreach (var command in commands)
        {
            command.Undo = true;
            var handler = context.CommandHandlers.Single(h => h.CanHandle(command));
            await handler.Execute(command, context);
        }
        
        await _changePusher.Enqueue(commands);
        _future.Push(memento);
        Changed.Invoke();
        return success;
    }
    
    public async Task<bool> TryRedo(DiagramContext context)
    {
        _logger.LogInformation("Redoing history");

        var success = _future.TryPop(out var memento);
        if (!success || memento is null) return success;

        var commands = memento.Commands
            .Reverse()
            .ToArray();
        foreach (var command in commands)
        {
            command.Undo = false;
            var handler = context.CommandHandlers.Single(h => h.CanHandle(command));
            await handler.Execute(command, context);
        }
        
        await _changePusher.Enqueue(commands);
        _history.Push(memento);
        Changed.Invoke();
        return success;
    }
}