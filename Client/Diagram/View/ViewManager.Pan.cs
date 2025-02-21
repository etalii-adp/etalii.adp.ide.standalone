using System.Reactive.Linq;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private void InitializePan()
    {
        _view.SetPan(_diagram.Position.X, _diagram.Position.Y);

        // Convert the event into an observable sequence
        var eventStream = Observable
            .FromEvent(
                h => _view.PanChanged += h, 
                h => _view.PanChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500)); // Waits for 500ms of inactivity

        eventStream.Subscribe(_ => OnDiagramPanned());
    }

    private async void OnDiagramPanned()
    {
        try
        {
            var newPosition = new DiagramPosition { X = _view.Pan.X, Y = _view.Pan.Y };
            
            _logger.LogInformation("Panning diagram to {DiagramPosition}", newPosition);

            var change = DiagramPositionChange.Apply(_diagram, newPosition);

            // Throttled save.
            await _history.Push(change);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {MethodName}", nameof(OnDiagramPanned));
        }
    }

}
    
