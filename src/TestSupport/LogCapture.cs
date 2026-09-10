using System.Globalization;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Xunit;

namespace EtAlii.Adp;

/// <summary>
/// Collects what the TEST THAT STARTED IT caused to be logged, so it can assert on what the
/// code under test said - and nothing any other test logged while it was open.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it captures:</b> every event written while the capture is open by code running on
/// the starting test's own flow - the test method, its constructor, the subject, every
/// collaborator the subject calls whatever source context it logs under, and work the test
/// awaits on the thread pool, since that carries the test's execution context with it.
/// </para>
/// <para>
/// <b>What it does not:</b> events from other tests, however they overlap in time; and events
/// logged on a thread that does NOT carry the test's execution context - a
/// <see cref="FileSystemWatcher"/> callback, a thread started under
/// <see cref="ExecutionContext.SuppressFlow"/>. Such an event belongs to no test, so no
/// capture sees it. That is the class this design gives up in exchange for isolation; a test
/// whose subject warns from such a thread cannot assert on it here.
/// </para>
/// <para>
/// <b>Why it is scoped by test and not by schedule.</b> The pipeline is process-wide - the
/// code under test binds its static <c>Log.ForContext&lt;T&gt;()</c> fields to whatever
/// <see cref="Log.Logger"/> was when they were first touched, so the module initializer below
/// installs one pipeline for the whole assembly before anything runs. This class used to hold
/// ONE active capture on that shared pipeline, isolated only by every class that logs or
/// captures naming <see cref="Collection"/>. That failed two ways. A test that logged without
/// joining dropped its warnings into someone else's capture - eighteen of nineteen test
/// classes in the databricks assembly never joined, because every class that exercises code
/// which warns "logs", which is most of them, so the rule could not be kept - and
/// <c>DatabricksDocumentStoreMissingBodyTests</c> failed its <c>Assert.Empty</c> in a full
/// run while passing alone. And a concurrent <see cref="Start"/> REPLACED the active capture
/// rather than joining it, so the first stopped receiving its own events and presence
/// assertions could fail too. Both are pinned deterministically in
/// <c>EtAlii.Adp.Tests/LogCapture.Tests.cs</c>. Scoping by the test that started the capture
/// needs no membership anyone has to remember.
/// </para>
/// </remarks>
public sealed class LogCapture : IDisposable
{
    /// <summary>
    /// An xUnit collection some classes still name. It no longer isolates anything - a capture
    /// now collects only its own test's events whether or not its class joins - and a new
    /// class need not name it. Kept only because existing classes do.
    /// </summary>
    public const string Collection = "Serilog pipeline";

    /// <summary>Renders a message the way the host's console sink does, so what a test asserts on is what an operator reads.</summary>
    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    private static readonly Lock Gate = new();

    /// <summary>Every open capture - several at once when tests run in parallel, each with its own owner.</summary>
    private static readonly List<LogCapture> Open = [];

    /// <summary>The test that started this capture, or null when it was started outside any test.</summary>
    private readonly string? _owner;

    private readonly List<LogEvent> _events = [];

    /// <summary>
    /// Collects one event into every open capture belonging to the test that is running where
    /// the event was logged, dropping it when there is none. Called by
    /// <see cref="LogCaptureCaptureSink"/>, which sits beside this class rather than inside it
    /// (tech.md is no-nested-types rule) and so cannot reach the open captures directly.
    /// </summary>
    internal static void Receive(LogEvent logEvent)
    {
        var test = CurrentTest();
        lock (Gate)
        {
            foreach (var capture in Open)
            {
                if (string.Equals(capture._owner, test, StringComparison.Ordinal))
                {
                    capture._events.Add(logEvent);
                }
            }
        }
    }

    private LogCapture(string? owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Starts collecting the running test's events; dispose to stop. Any number may be open at
    /// once, and each sees only its own test.
    /// </summary>
    public static LogCapture Start()
    {
        var capture = new LogCapture(CurrentTest());
        lock (Gate)
        {
            Open.Add(capture);
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
            Open.Remove(this);
        }
    }

    /// <summary>
    /// The test running on this flow - the same for its constructor and its method, which
    /// xUnit v3 keeps in one context - or null on a flow no test started.
    /// </summary>
    private static string? CurrentTest() => TestContext.Current.Test?.UniqueID;

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
