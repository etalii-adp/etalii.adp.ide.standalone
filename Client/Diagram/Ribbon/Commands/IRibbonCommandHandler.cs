using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public interface IRibbonCommandHandler : ICommandHandler
{
    IconName IconName { get; }
    string IconTitle { get; }
    IconColor IconColor { get; }

    string CommandName { get; }

    Command[] CreateCommands(DiagramContext context);
    
    bool CanHandle(DiagramContext context);
}

public interface IRibbonCommandHandler<TCommand> : IRibbonCommandHandler
    where TCommand : Command   
{
    string IRibbonCommandHandler.CommandName => typeof(TCommand).Name;
}