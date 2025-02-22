namespace EtAlii.Adp.Client;

using Cn = CommandName;

public partial class PanCommandHandler : CommandHandler<DiagramPositionCommand>
{
    public override string CommandName => Cn.Pan;
    private readonly ILogger _logger;
    
    public PanCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PanCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext context)
    {
        return new DiagramPositionCommand 
        { 
            Id = context.Diagram.Id, 
            NewPosition = new DiagramPosition { X = context.View.Pan.X, Y = context.View.Pan.Y },
            OldPosition = context.Diagram.Position,
        };
    }
    
    protected override Task Do(DiagramPositionCommand command, DiagramContext context)
    {
        StopPanningMonitor();
        
        _logger.LogInformation("Panning diagram from {FromDiagramPosition} to {ToDiagramPosition}", command.OldPosition, command.NewPosition);
        context.Diagram.Position = command.NewPosition; 
        context.View.SetPan(command.NewPosition.X, command.NewPosition.Y);

        StartPanningMonitor();
        return Task.CompletedTask;
    }

    protected override Task Undo(DiagramPositionCommand command, DiagramContext context)
    {
        StopPanningMonitor();
        
        _logger.LogInformation("Panning diagram from {FromDiagramPosition} to {ToDiagramPosition}", command.NewPosition, command.OldPosition);
        context.Diagram.Position = command.OldPosition; 
        context.View.SetPan(command.OldPosition.X, command.OldPosition.Y);

        StartPanningMonitor();
        return Task.CompletedTask;
    }
}