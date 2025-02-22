namespace EtAlii.Adp.Client;
using Cn = CommandName;

public partial class ZoomCommandHandler : CommandHandler<DiagramZoomCommand>
{
    public override string CommandName => Cn.Zoom;

    private readonly ILogger _logger;

    public ZoomCommandHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ZoomCommandHandler>();
    }

    public static Command CreateCommand(DiagramContext context)
    {
        return new DiagramZoomCommand
        {
            Id = context.Diagram.Id, 
            NewZoom = context.View.Zoom, 
            OldZoom = context.Diagram.Zoom
        };
    }

    protected override Task Do(DiagramZoomCommand command, DiagramContext context)
    {
        StopZoomMonitor();

        _logger.LogInformation("Zooming diagram from {FromZoom} to {ToZoom}", command.OldZoom, command.NewZoom);
        context.Diagram.Zoom = command.NewZoom;
        context.View.SetZoom(command.NewZoom);
        
        StartZoomMonitor();
        return Task.CompletedTask;
    }

    protected override Task Undo(DiagramZoomCommand command, DiagramContext context)
    {
        StopZoomMonitor();

        _logger.LogInformation("Zooming diagram from {FromZoom} to {ToZoom}", command.NewZoom, command.OldZoom);
        context.Diagram.Zoom = command.OldZoom;
        context.View.SetZoom(command.OldZoom);

        StartZoomMonitor();
        return Task.CompletedTask;
    }

}