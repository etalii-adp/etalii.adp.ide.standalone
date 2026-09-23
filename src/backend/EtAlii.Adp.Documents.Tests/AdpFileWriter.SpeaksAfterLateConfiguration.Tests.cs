using Serilog;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The failure record reaches the log even when Serilog was configured AFTER this class was first
/// used — which is the ordinary case in a host, and the case that silenced it in the field.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect, measured.</b> Serilog's unset <c>Log.Logger</c> is a <c>SilentLogger</c>; a
/// <c>private static readonly ILogger</c> evaluated then IS that silent logger for the life of the
/// process, and configuring <c>Log.Logger</c> afterwards changes nothing for it. Type-initialisation
/// order is first-use order, so the instrument was wired in some runs and not others.
/// </para>
/// <para>
/// <b>What this guard can and cannot do.</b> It cannot drive the real trigger — a type initialises
/// once per process, and in this process it already has. So it asserts the BEHAVIOUR that the fix
/// buys: a logger configured after the fact reaches the record. Its perturbation is the defect's
/// own shape, a logger captured while Serilog was unset, which is exactly what occurrence five
/// looked like from the outside.
/// </para>
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class AdpFileWriterSpeaksAfterLateConfigurationTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-late-config-" + Guid.NewGuid().ToString("N"));
    private readonly ILogger _original = Log.Logger;

    public AdpFileWriterSpeaksAfterLateConfigurationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Log.Logger = _original;
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFailedPublish_IsRecorded_WhenSerilogWasConfiguredAfterTheWriterWasFirstUsed()
    {
        // Arrange: the writer is used while Serilog is unset - a host that touches it before
        // Program.cs builds the pipeline - and only then is a real logger installed.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        Log.CloseAndFlush(); // Serilog's unset state: Log.Logger is a SilentLogger
        AdpFileWriter.Save(path, "a save while nothing is listening");

        var heard = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new QueueSink(heard))
            .CreateLogger();

        // Act: a publish that fails, after configuration.
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // Assert: the record arrived. With a logger cached before configuration it does not, and
        // nothing else about the run looks different - which is how five occurrences were read as
        // "no second actor named" while one of them had no instrument at all.
        Assert.Contains(heard, line => line.Contains("Could not publish", StringComparison.Ordinal));
        Assert.Contains(heard, line => line.Contains("0x80070497", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHolderQuery_AlsoSpeaksAfterLateConfiguration()
    {
        // FileHolders holds its own logger and had the same defect; its late-answer line is the one
        // that distinguishes a slow query from one that never answered, so its silence would be
        // indistinguishable from "never answered".
        FileHolders.Budget = TimeSpan.FromMilliseconds(50);
        using var answer = new ManualResetEventSlim(false);
        FileHolders.Query = _ =>
        {
            answer.Wait(TimeSpan.FromSeconds(10));
            return "pid 4242 someone.exe";
        };

        Log.CloseAndFlush();
        var described = FileHolders.Describe(IoPath.Combine(_folder, "roadmap.mm"));
        Assert.Contains("not yet known", described, StringComparison.Ordinal);

        var heard = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new QueueSink(heard))
            .CreateLogger();

        answer.Set();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !heard.Any(line => line.Contains("pid 4242 someone.exe", StringComparison.Ordinal)))
        {
            Thread.Sleep(25);
        }

        Assert.Contains(heard, line => line.Contains("pid 4242 someone.exe", StringComparison.Ordinal));
        FileHolders.Query = null;
        FileHolders.Budget = TimeSpan.FromSeconds(2);
    }

    private sealed class QueueSink(System.Collections.Concurrent.ConcurrentQueue<string> heard) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => heard.Enqueue(logEvent.RenderMessage());
    }
}
