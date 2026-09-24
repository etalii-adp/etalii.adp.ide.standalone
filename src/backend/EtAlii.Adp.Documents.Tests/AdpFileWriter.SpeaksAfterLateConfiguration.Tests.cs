using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The failure record reaches a Serilog pipeline installed AFTER this class was first used - which
/// is the ordinary case in a host, and the case that silenced the instrument in the field.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect, measured.</b> Serilog's unset <c>Log.Logger</c> is a <c>SilentLogger</c>, and a
/// <c>private static readonly ILogger</c> evaluated at that moment IS that silent logger for the
/// life of the process; configuring <c>Log.Logger</c> afterwards changes nothing for it.
/// Type-initialisation order is first-use order, so the instrument was wired in some runs and not
/// others.
/// </para>
/// <para>
/// <b>NO TEST IN THIS ASSEMBLY CAN REPRODUCE THE FIELD CONDITION, and that is worth knowing.</b>
/// <see cref="LogCapture"/> installs one pipeline from a module initializer before anything else
/// runs, precisely so that no static logger field ever binds to the silent logger. The production
/// defect therefore cannot occur here at all: the suite is structurally blind to it, which is a
/// better account of why every gate stayed green while the field went silent than any amount of
/// care would be. What this class CAN pin is the property the fix buys - a logger resolved at the
/// call site follows a pipeline installed later, and a cached one does not.
/// </para>
/// <para>
/// <b>Why the replacement pipeline keeps the capture sink.</b> The first version of this class
/// called <c>Log.CloseAndFlush()</c> and installed a bare pipeline of its own. That starved every
/// capture running beside it - the pipeline is the shared substrate all of them ride - and failed
/// seven tests in three other classes with empty collections on a full run, while passing 117 of
/// 117 alone. A collection would not have fixed it: LogCapture's own remarks record that
/// membership is the rule nobody can keep, eighteen of nineteen classes having never joined. So
/// the replacement carries <see cref="LogCaptureCaptureSink"/> beside this class's own sink, and
/// <see cref="ACaptureOpenedBeforeTheReplacement_StillCollectsAfterIt"/> is the guard that says so.
/// </para>
/// </remarks>
[Collection(FileHoldersSeam.Name)]
public class AdpFileWriterSpeaksAfterLateConfigurationTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-late-config-" + Guid.NewGuid().ToString("N"));
    private readonly ILogger _original = Log.Logger;

    public AdpFileWriterSpeaksAfterLateConfigurationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        // Restored, never disposed: the original pipeline is the assembly's one substrate and it
        // outlives every test in it.
        Log.Logger = _original;
        FileHolders.Query = null;
        FileHolders.Budget = TimeSpan.FromSeconds(2);
        TestFolder.TryDelete(_folder);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Installs a pipeline as a host would at startup, WITHOUT taking the substrate away from the
    /// captures already open in other tests, and returns what this class hears on it.
    /// </summary>
    private static ConcurrentQueue<string> ConfigureSerilogLate()
    {
        var heard = new ConcurrentQueue<string>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new LogCaptureCaptureSink())
            .WriteTo.Sink(new QueueSink(heard))
            .CreateLogger();
        return heard;
    }

    [Fact]
    public void AFailedPublish_IsRecorded_OnAPipelineInstalledAfterTheWriterWasFirstUsed()
    {
        // Arrange. The writer is used FIRST, binding anything it caches to the pipeline of the
        // moment, and only then is a new one installed - a host that touches the writer before
        // Program.cs builds its logging.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        File.WriteAllText(path, "before");
        AdpFileWriter.Save(path, "a save before the pipeline changed");

        var heard = ConfigureSerilogLate();

        // Act. A publish that fails, after the change.
        var wild = new IOException("Unable to remove the file to be replaced.") { HResult = unchecked((int)0x80070497) };
        Assert.Throws<IOException>(() => AdpFileWriter.Save(path, "after", replace: (_, _) => throw wild));

        // Assert. The record arrived HERE, on the new pipeline. A cached logger would still be
        // writing to the old one, where this sink is not - and nothing else about the run would
        // look different, which is how five occurrences were read as "no second actor named"
        // while one of them had no instrument at all.
        Assert.Contains(heard, line => line.Contains("Could not publish", StringComparison.Ordinal));
        Assert.Contains(heard, line => line.Contains("0x80070497", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHolderQuery_AlsoSpeaksOnAPipelineInstalledLate()
    {
        // FileHolders holds its own logger and had the same defect. Its late-answer line is what
        // distinguishes a slow query from one that never answered, so losing it would make those
        // two indistinguishable - the reading this instrument exists to separate.
        var path = IoPath.Combine(_folder, "roadmap.mm");
        FileHolders.Budget = TimeSpan.FromMilliseconds(50);
        using var answer = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        FileHolders.Query = _ =>
        {
            // ReSharper disable AccessToDisposedClosure
            // Reason: Used in a test case which is acceptable.
            entered.Set();
            answer.Wait(Patience);
            // ReSharper restore AccessToDisposedClosure
            return "pid 4242 someone.exe";
        };

        var described = FileHolders.Describe(path);
        Assert.Contains("not yet known", described, StringComparison.Ordinal);
        Assert.True(
            entered.Wait(Patience, TestContext.Current.CancellationToken),
            "The holder query was never entered, so nothing below would be about a late answer.");

        var heard = ConfigureSerilogLate();
        answer.Set();

        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline && !heard.Any(line => line.Contains("pid 4242 someone.exe", StringComparison.Ordinal)))
        {
            Thread.Sleep(25);
        }

        Assert.Contains(heard, line => line.Contains("pid 4242 someone.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void ACaptureOpenedBeforeTheReplacement_StillCollectsAfterIt()
    {
        // THE GUARD FOR THE REGRESSION THIS CLASS CAUSED, and the reason it is a test rather than
        // a comment. Replacing Log.Logger is a process-wide act: every capture in the assembly
        // rides the pipeline being replaced, so a replacement that drops LogCaptureCaptureSink
        // silences all of them for its window. That failed seven tests in three unrelated classes
        // and cost a gate, while this project passed alone. It fails HERE now, loudly and in the
        // class responsible, if anyone simplifies ConfigureSerilogLate back to a bare pipeline.
        using var substrate = LogCapture.Start();
        var sentinel = Guid.NewGuid().ToString("N");

        ConfigureSerilogLate();

        Log.ForContext("SourceContext", "Some.Other.Test").Warning("Logged after the replacement {Sentinel}", sentinel);

        Assert.Contains(substrate.Warnings, warning => warning.Contains(sentinel, StringComparison.Ordinal));
    }

    private sealed class QueueSink(ConcurrentQueue<string> heard) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => heard.Enqueue(logEvent.RenderMessage());
    }
}
