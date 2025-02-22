using System.Reactive;
using System.Reactive.Linq;

namespace EtAlii.Adp.Client;

public partial class PanCommandHandler 
{
    private DiagramContext _context = null!;
    private IObservable<Unit> _eventStream = null!;
    private IDisposable _subscription = null!;

    public void Initialize(DiagramContext context)
    {
        _context = context;
        context.View.SetPan(context.Diagram.Position.X, context.Diagram.Position.Y);

        // Convert the event into an observable sequence
        _eventStream = Observable
            .FromEvent(
                h => context.View.PanChanged += h, 
                h => context.View.PanChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500)); // Waits for 500ms of inactivity
        
        StartPanningMonitor();
    }

    private void StartPanningMonitor()
    {
        _subscription = _eventStream.Subscribe(_ =>
        {
            var command = CreateCommand(_context);
            _context.Commands.Handle(command);
        });
    }
    private void StopPanningMonitor()
    {
        _subscription.Dispose();
        _subscription = null!;
    }
}