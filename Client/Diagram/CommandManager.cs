namespace EtAlii.Adp.Client;

public class CommandManager
{
    private readonly HistoryManager _history;
    private DiagramContext _context = null!;
    private readonly ILogger _logger;

    public CommandManager(HistoryManager history, ILoggerFactory loggerFactory)
    {
        _history = history;
        _logger = loggerFactory.CreateLogger<CommandManager>();

    }

    public async void Handle(Command command)
    {
        try
        {
            var commandName = command.GetType().Name;
            _logger.LogInformation("Handling {CommandName} command", commandName);

            await _history.Push(command, _context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle command");
        }
    }

    public async void Handle(Command[] commands)
    {
        try
        {
            var commandNames = string.Join(", ", commands.Select(c => c.GetType().Name));
            _logger.LogInformation("Handling {CommandName} command", commandNames);

            await _history.Push(commands, _context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle commands");
        }
    }

    public void Initialize(DiagramContext context)
    {
        _context = context;
    }
    
    public void Deinitialize()
    {
        // Placeholder.
    }
}