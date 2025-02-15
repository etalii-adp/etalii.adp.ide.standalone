namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    private async void OnDiagramZoomed()
    {
        try
        {
            var change = MapZoomChange.Apply(_diagram, (float)Diagram.Zoom);

            // Throttled save.
            await ChangePusher.Enqueue(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramZoomed));
        }
    }

    private async void OnDiagramPanned()
    {
        try
        {
            var newPosition = new DiagramPosition { X = Diagram.Pan.X, Y = Diagram.Pan.Y };
            var change = MapPositionChange.Apply(_diagram, newPosition);

            // Throttled save.
            await ChangePusher.Enqueue(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramPanned));
        }
    }
}