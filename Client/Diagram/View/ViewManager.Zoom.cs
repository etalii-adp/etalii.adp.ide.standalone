using System.Reactive.Linq;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private void InitializeZoom()
    {
        _view.SetZoom(_diagram.Zoom <= 0f ? 1f : _diagram.Zoom);

        // Convert the event into an observable sequence
        var eventStream = Observable
            .FromEvent(
                h => _view.ZoomChanged += h, 
                h => _view.ZoomChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500)); // Waits for 500ms of inactivity

        eventStream.Subscribe(_ => HandleCommand(CommandName.Zoom));
    }
}
    
