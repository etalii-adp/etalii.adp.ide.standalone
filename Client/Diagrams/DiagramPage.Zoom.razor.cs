using System.Reactive.Linq;

namespace EtAlii.Adp.Client;

public partial class DiagramPage
{
    private void InitializeZoom()
    {
        Diagram.SetZoom(_diagram.Zoom <= 0f ? 1f : _diagram.Zoom);

        // Convert the event into an observable sequence
        var eventStream = Observable
            .FromEvent(
                h => Diagram.ZoomChanged += h, 
                h => Diagram.ZoomChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500)); // Waits for 500ms of inactivity

        eventStream.Subscribe(_ => OnDiagramZoomed());
    }

    private async void OnDiagramZoomed()
    {
        try
        {
            _logger.LogInformation("Zooming diagram from {OldZoom} to {NewZoom}", _diagram.Zoom, Diagram.Zoom);

            var change = DiagramZoomChange.Apply(_diagram, Diagram.Zoom);

            // Throttled save.
            await ChangePusher.Enqueue(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramZoomed));
        }
    }
}