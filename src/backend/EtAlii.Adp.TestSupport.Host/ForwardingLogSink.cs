using Serilog.Core;
using Serilog.Events;

namespace EtAlii.Adp.TestSupport.Host;

/// <summary>
/// Writes every event a host's pipeline receives into another logger as well - the one that was
/// global before any host ran, so that <c>LogCapture</c> still hears it (see
/// <see cref="HostFixture"/>).
/// </summary>
/// <remarks>
/// Not disposable on purpose: a class whose static logger bound to a host's pipeline keeps writing
/// through it after that host has stopped, and those events still have to arrive.
/// </remarks>
internal sealed class ForwardingLogSink(Serilog.ILogger target) : ILogEventSink
{
    public void Emit(LogEvent logEvent) => target.Write(logEvent);
}
