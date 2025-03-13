using System.Net.Http.Json;
using NeoSmart.AsyncLock;

namespace EtAlii.Adp.Client;

public class ChangePusher
{
    private readonly Queue<Command> _commands = new();
    private Task? _pushTask;

    private readonly HttpClient _client;
    private readonly AsyncLock _lock = new();
    
    private readonly Type[] _takeLastCommandTypes =
    [
        typeof(DiagramZoomCommand),
        typeof(DiagramPositionCommand)
    ];

    private readonly ILogger<ChangePusher> _logger;

    public ChangePusher(
        HttpClient client, 
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _logger = loggerFactory.CreateLogger<ChangePusher>();
    }

    public async Task Enqueue(Command command, DiagramIdentifier diagramId)
    {
        using (await _lock.LockAsync())
        {
            _logger.LogInformation("Enqueuing {CommandCount} commands to be pushed to the backend", 1);

            _commands.Enqueue(command);
            _pushTask ??= Task.Run(() => PushChanges(diagramId));
        }
    }

    public async Task Enqueue(Command[] commands, DiagramIdentifier diagramId)
    {
        using (await _lock.LockAsync())
        {
            _logger.LogInformation("Enqueuing {CommandCount} commands to be pushed to the backend", commands.Length);

            foreach (var command in commands)
            {
                _commands.Enqueue(command);
            }
            _pushTask ??= Task.Run(() => PushChanges(diagramId));
        }
    }

    private async Task PushChanges(DiagramIdentifier diagramId)
    {
        Command[] commandsToPush;
        do
        {
            bool commandsGotPushed;
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
                commandsGotPushed = await PushChanges(commandsToPush, diagramId);
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

    private async Task<bool> PushChanges(Command[] commands, DiagramIdentifier diagramId)
    {
        commands = Flatten(commands);

        try
        {
            var response = await _client.PostAsJsonAsync(ApplicationApi.Diagrams.Changes.Request(diagramId), commands);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to post commands to the backend: {ExceptionMessage}", e.Message);
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