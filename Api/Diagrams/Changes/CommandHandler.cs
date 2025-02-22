namespace EtAlii.Adp.Api;

public abstract class CommandHandler<TCommand> : ICommandHandler
    where TCommand : Command
{
    public Type CommandType { get; } = typeof(TCommand);

    Task ICommandHandler.Apply(Command command, AdpDbContext context)
    {
        return command.Undo 
            ? Undo((TCommand)command, context) 
            : Do((TCommand)command, context);
    }
    
    protected abstract Task Do(TCommand change, AdpDbContext context);
    protected abstract Task Undo(TCommand change, AdpDbContext context);
}