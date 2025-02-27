using System.Reactive;
using System.Reactive.Linq;

namespace EtAlii.Adp.Client;

public partial class ZoomCommandHandler 
{
    private const double _zoomTolerance = 0.00001f;
    private DiagramContext _context = null!;
    private IObservable<Unit> _eventStream = null!;
    private IDisposable _subscription = null!;

    public void Initialize(DiagramContext context)
    {
        _context = context;
        context.View.SetZoom(context.Diagram.Zoom <= 0f ? 1f : context.Diagram.Zoom);

        // Convert the event into an observable sequence
        _eventStream = Observable
            .FromEvent(
                h => context.View.ZoomChanged += h, 
                h => context.View.ZoomChanged -= h)
            .Throttle(TimeSpan.FromMilliseconds(500)); // Waits for 500ms of inactivity
        
        StartZoomMonitor();
    }

    public void DeInitialize()
    {
        StopZoomMonitor();
    }

    private void StartZoomMonitor()
    {
        _subscription = _eventStream.Subscribe(_ =>
        {
            var newZoom = _context.View.Zoom;
            var oldZoom = _context.Diagram.Zoom;
            if (!(Math.Abs(newZoom - oldZoom) > _zoomTolerance)) return;
            
            var command = CreateCommand(_context);
            _context.Commands.Handle(command);
        });
    }
    private void StopZoomMonitor()
    {
        _subscription.Dispose();
        _subscription = null!;
    }
}