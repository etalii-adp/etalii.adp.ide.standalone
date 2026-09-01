using System.Globalization;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace EtAlii.Adp.Editor.Tests;

/// <summary>
/// Collects everything written to Serilog while it is active, so a test can assert on what
/// the code under test said.
/// </summary>
/// <remarks>
/// The code under test logs through <c>Log.ForContext&lt;T&gt;()</c> held in a static field,
/// which binds to whatever <see cref="Log.Logger"/> was when that field was first touched -
/// so the pipeline has to be in place before any of it runs. The module initializer below
/// does that once for the whole test assembly, and each capture only decides where the
/// events it receives are collected.
/// <para>
/// That single pipeline is shared, so two captures alive at once would each see the other's
/// events, and a class logging without capturing would drop its events into someone else's
/// capture. Every class that logs or captures therefore names <see cref="Collection"/>,
/// which is what keeps them from running at the same time.
/// </para>
/// </remarks>
public sealed class LogCapture : IDisposable
{
    /// <summary>The xUnit collection every class that logs or captures belongs to, so none of them overlap.</summary>
    public const string Collection = "Serilog pipeline";

    /// <summary>Renders a message the way the host's console sink does, so what a test asserts on is what an operator reads.</summary>
    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    private static readonly Lock Gate = new();
    private static LogCapture? _active;

    private readonly List<LogEvent> _events = [];

    /// <summary>
    /// Collects one event into whichever capture is active, dropping it when none is. Called
    /// by <see cref="LogCaptureCaptureSink"/>, which sits beside this class rather than inside
    /// it (tech.md is no-nested-types rule) and so cannot reach the gate or the active capture
    /// directly.
    /// </summary>
    internal static void Receive(LogEvent logEvent)
    {
        lock (Gate)
        {
            _active?._events.Add(logEvent);
        }
    }

    private LogCapture()
    {
    }

    /// <summary>Starts collecting; dispose to stop. Only one capture is active at a time.</summary>
    public static LogCapture Start()
    {
        var capture = new LogCapture();
        lock (Gate)
        {
            _active = capture;
        }

        return capture;
    }

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (Gate)
            {
                return _events.ToArray();
            }
        }
    }

    public IEnumerable<string> Errors => Rendered(LogEventLevel.Error);

    public IEnumerable<string> Warnings => Rendered(LogEventLevel.Warning);

    public IEnumerable<string> Informations => Rendered(LogEventLevel.Information);

    public void Dispose()
    {
        lock (Gate)
        {
            if (ReferenceEquals(_active, this))
            {
                _active = null;
            }
        }
    }

    private IEnumerable<string> Rendered(LogEventLevel level) =>
        Events.Where(logEvent => logEvent.Level == level).Select(Render).ToArray();

    private static string Render(LogEvent logEvent)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        Formatter.Format(logEvent, writer);
        return writer.ToString();
    }

    /// <summary>
    /// Installs the capturing pipeline before any test - or any static logger field in the
    /// code under test - has had a chance to run.
    /// </summary>
    [ModuleInitializer]
    internal static void Install() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new LogCaptureCaptureSink())
            .CreateLogger();

}
