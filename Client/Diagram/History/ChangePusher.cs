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

    private readonly ILogger<ChangePusher> _logger;

    public ChangePusher(
        HttpClient client, 
        DiagramManager diagramManager,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _diagramManager = diagramManager;
        _logger = loggerFactory.CreateLogger<ChangePusher>();
    }

    public async Task Enqueue(Command command)
    {
        using (await _lock.LockAsync())
        {
            _logger.LogInformation("Enqueuing {CommandCount} commands to be pushed to the backend", 1);

            _commands.Enqueue(command);
            _pushTask ??= Task.Run(PushChanges);
        }
    }

    public async Task Enqueue(Command[] commands)
    {
        using (await _lock.LockAsync())
        {
            _logger.LogInformation("Enqueuing {CommandCount} commands to be pushed to the backend", commands.Length);

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
            var commandsGotPushed = false;
            using (await _lock.LockAsync())
            {
                commandsToPush = _commands.ToArray();
                _commands.Clear();

                if (!commandsToPush.Any())
                {
                    _logger.LogInformation("No commands available to send to to the backend");
                    continue;
                }
                
                _logger.LogInformation("Pushing {CommandCount} commands to the backend", commandsToPush.Length);
                commandsGotPushed = await PushChanges(commandsToPush);
                if (commandsGotPushed) continue;

                // Post failed, re-adding them to the queue.
                foreach (var command in commandsToPush)
                {
                    _commands.Enqueue(command);
                }
            }
            if (!commandsGotPushed)
            {
                _logger.LogInformation("Delaying check for change detection");

                await Task.Delay(TimeSpan.FromSeconds(1)); // Let's wait one second and try again.
                // We do this outside of the lock as we do not want the application to freeze up.
            }
            
        } while (commandsToPush.Any());

        using (await _lock.LockAsync())
        {
            _pushTask = null;
        }
    }

    private async Task<bool> PushChanges(Command[] commands)
    {
        commands = Flatten(commands);

        try
        {
            await _client.PostAsJsonAsync(ApplicationApi.Diagrams.Changes.Request(_diagramManager.CurrentDiagram!.Id), commands);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError("Unable to post commands to the backend");
            _logger.LogError(e, e.Message);
            return false;
        }
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