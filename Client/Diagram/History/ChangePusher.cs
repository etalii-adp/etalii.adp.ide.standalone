using System.Net.Http.Json;
using NeoSmart.AsyncLock;

namespace EtAlii.Adp.Client;

public class ChangePusher
{
    private readonly Queue<Command> _commands = new();
    private Task? _pushTask;

    private readonly HttpClient _client;
    private readonly AsyncLock _lock = new();
    
    private readonly DiagramManager _diagramManager;
    
    private readonly Type[] _takeLastCommandTypes =
    [
        typeof(DiagramZoomCommand),
        typeof(DiagramPositionCommand)
    ];

    public ChangePusher(HttpClient client, DiagramManager diagramManager)
    {
        _client = client;
        _diagramManager = diagramManager;
    }

    public async Task Enqueue(Command command)
    {
        using (await _lock.LockAsync())
        {
            _commands.Enqueue(command);
            _pushTask ??= Task.Run(PushChanges);
        }
    }

    public async Task Enqueue(Command[] commands)
    {
        using (await _lock.LockAsync())
        {
            foreach (var command in commands)
            {
                _commands.Enqueue(command);
            }
            _pushTask ??= Task.Run(PushChanges);
        }
    }

    private async Task PushChanges()
    {
        Command[] commandsToPush;
        do
        {
            using (await _lock.LockAsync())
            {
                commandsToPush = _commands.ToArray();
                _commands.Clear();
            }

            if (commandsToPush.Any())
            {
                await PushChanges(commandsToPush);
            }
            
        } while (commandsToPush.Any());

        using (await _lock.LockAsync())
        {
            _pushTask = null;
        }
    }

    private async Task PushChanges(Command[] commands)
    {
        commands = Flatten(commands);
        
        await _client.PostAsJsonAsync(ApplicationApi.Diagrams.Changes.Request(_diagramManager.CurrentDiagram!.Id), commands);
    }

    private Command[] Flatten(Command[] commands)
    {
        foreach (var takeLastCommandType in _takeLastCommandTypes)
        {
            var last = commands.LastOrDefault(c => c.GetType() == takeLastCommandType);
            if (last != null)
            {
                commands = commands
                    .Where(c => c.GetType() != takeLastCommandType)
                    .Concat([last])
                    .ToArray();
            }
        }
        return commands;
    }
}