namespace EtAlii.Adp.Client;

public interface ICommandHandler
{
    string CommandName { get; }

    bool CanHandle(Command command);
    
    bool SendToBackend { get; }
    bool UseInUndoRedo { get; }
    
    Task Execute(Command command, DiagramContext context);
}