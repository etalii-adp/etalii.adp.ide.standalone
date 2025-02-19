namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class DeleteCommandHandler : ICommandHandler
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly ChangePusher _changePusher;

    public DeleteCommandHandler(DiagramView view, Diagram diagram, ChangePusher changePusher)
    {
        _view = view;
        _diagram = diagram;
        _changePusher = changePusher;
    }

    public string CommandName => Cn.Delete;

    public void Execute()
    {
        
    }
}