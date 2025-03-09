using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public abstract class RibbonCommandHandler<TCommand> : CommandHandler<TCommand>, IRibbonCommandHandler<TCommand>
    where TCommand : Command
{
    public abstract IconName IconName { get; }
    public abstract string IconTitle { get; }
    public virtual IconColor IconColor => IconColor.Primary;

    public event Action Changed = null!;

    public event Action<Command[]> Clicked = null!;
    
    protected void RaiseChanged()
    {
        Changed.Invoke();
    }

    public void RaiseClicked(Command[] commands)
    {
        Clicked.Invoke(commands);
    }

    public abstract Command[] CreateCommands(DiagramContext context);
    
    public abstract bool CanHandle(DiagramContext context);

    public virtual bool IsToggled(DiagramContext context) => false;
}