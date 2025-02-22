namespace EtAlii.Adp.Client;

public abstract class CommandHandler<TCommand> : ICommandHandler
    where TCommand : Command
{
    public abstract string CommandName { get; }

    public bool CanHandle(Command command) => command is TCommand;

    public virtual bool SendToBackend => true;
    public virtual bool UseInUndoRedo => true;
    
    Task ICommandHandler.Execute(Command command, DiagramContext context)
    {
        return command.Undo 
            ? Undo((TCommand)command, context) 
            : Do((TCommand)command, context);
    }
    
    protected abstract Task Do(TCommand command, DiagramContext context);
    protected abstract Task Undo(TCommand command, DiagramContext context);

}