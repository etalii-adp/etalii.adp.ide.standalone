using Serilog.Core;
using Serilog.Events;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>Hands each event to whichever capture is active, and drops it when none is.</summary>
internal sealed class LogCaptureCaptureSink : ILogEventSink
{
    public void Emit(LogEvent logEvent) => LogCapture.Receive(logEvent);
}
