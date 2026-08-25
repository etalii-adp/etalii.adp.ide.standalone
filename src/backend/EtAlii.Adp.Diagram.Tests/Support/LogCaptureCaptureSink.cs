using System.Globalization;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>Hands each event to whichever capture is active, and drops it when none is.</summary>
internal sealed class LogCaptureCaptureSink : ILogEventSink
{
    public void Emit(LogEvent logEvent) => LogCapture.Receive(logEvent);
}
