using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public interface IRibbonCommandHandler : ICommandHandler
{
    IconName IconName { get; }
    string IconTitle { get; }
    IconColor IconColor { get; }

    string CommandName { get; }
    bool IsToggled(DiagramContext context);
    
    event Action Changed;

    event Action<Command[]> Clicked;

    void RaiseClicked(Command[] commands);
    
    Command[] CreateCommands(DiagramContext context);
    
    bool CanHandle(DiagramContext context);
}

public interface IRibbonCommandHandler<TCommand> : IRibbonCommandHandler
    where TCommand : Command   
{
    string IRibbonCommandHandler.CommandName => typeof(TCommand).Name;
}