namespace EtAlii.Adp.Client;

public interface ICommandHandler
{
    bool CanHandle(Command command);
    
    bool SendToBackend { get; }
    bool UseInUndoRedo { get; }

    Task Execute(Command command, DiagramContext context);
}