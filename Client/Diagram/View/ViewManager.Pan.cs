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

        eventStream.Subscribe(_ => HandleCommand(CommandName.Pan));
    }
}
    
