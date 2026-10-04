using EtAlii.Adp.Diagram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.HostLogging.Tests;

/// <summary>
/// Stopping one host leaves every other live host logging, and starting one does not replace a
/// live host's pipeline with a bootstrap logger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> <c>UseSerilog</c> without <c>preserveStaticLogger</c> closes the GLOBAL logger when
/// its host is disposed, and <c>Program.cs</c> did the same in a <c>finally</c>. In a process with
/// several hosts, the global belongs to the one started last, so one host stopping silenced
/// another that was still serving. Measured with two hosts: the global was <c>SilentLogger</c> within
/// milliseconds and the live host's sink was disposed. A call-site logger then drops its line, and
/// a static logger first initialised then is silent for the process. Both turned up as failures of
/// the <c>0x80070497</c> publish race whose <c>AdpFileWriter</c> record never arrived.
/// </para>
/// <para>
/// Each host gets its own recording sink through DI (<c>Program.cs</c> reads sinks with
/// <c>ReadFrom.Services</c>), so a line that arrives names the pipeline that carried it. Every case
/// first shows its sink receiving a line, so a later absence is a finding and not a dead sink.
/// </para>
/// <para>
/// <b>The only class in its project, on purpose.</b> Its subject, <see cref="Log.Logger"/>, is
/// process-wide, and a test assembly runs as its own process. So here no other class's host can move
/// the global under these assertions, and each red against the old code is deterministic. Beside the
/// other integration tests, a check such as "B's write reaches B's sink" would pass or fail on their
/// timing. A serial collection in <c>EtAlii.Adp.Backend.Tests</c> would have bought the same
/// determinism, but only after every parallel class there had finished, on every gate's critical
/// path. A second class here that starts a host ends the guarantee.
/// </para>
/// </remarks>
public class HostLoggerLifecycleTests : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "adp-host-logger-" + Guid.NewGuid().ToString("N"));
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    // LogCapture's pipeline in this assembly: whatever a case does to the global, the tests that run
    // after it get it back.
    private readonly ILogger _original = Log.Logger;

    public HostLoggerLifecycleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
        Log.Logger = _original;
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StoppingTheOlderHost_LeavesTheNewerOneLogging()
    {
        (WebApplicationFactory<Program> a, _) = Start();
        (_, RecordingSink sinkB) = Start();
        AssertArrives(sinkB, "control");

        a.Dispose();

        AssertStillLogging(sinkB);
    }

    [Fact]
    public void StoppingTheNewerHost_HandsTheGlobalToTheOlderOne()
    {
        // The order only a list of live hosts gets right: the global belonged to B, and B is gone.
        (_, RecordingSink sinkA) = Start();
        (WebApplicationFactory<Program> b, RecordingSink sinkB) = Start();
        AssertArrives(sinkB, "control");

        b.Dispose();

        AssertStillLogging(sinkA);
    }

    [Fact]
    public async Task TwoHostsStoppingAtOnce_LeaveTheThirdLogging()
    {
        // Released together by a barrier rather than by timing, so the two releases really overlap.
        (WebApplicationFactory<Program> a, _) = Start();
        (WebApplicationFactory<Program> b, _) = Start();
        (_, RecordingSink sinkC) = Start();
        AssertArrives(sinkC, "control");
        using var together = new Barrier(2);

        await Task.WhenAll(
            Task.Run(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                // Reason: both tasks are awaited before the barrier is disposed.
                together.SignalAndWait(Settle);
                a.Dispose();
            }, TestContext.Current.CancellationToken),
            Task.Run(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                // Reason: both tasks are awaited before the barrier is disposed.
                together.SignalAndWait(Settle);
                b.Dispose();
            }, TestContext.Current.CancellationToken));

        AssertStillLogging(sinkC);
    }

    [Fact]
    public void StartingAHost_DoesNotReplaceALiveHostsPipelineWithABootstrapLogger()
    {
        // Written from inside C's build, which is where a class first used would bind. Asserted by
        // where the line ARRIVES rather than by which logger instance is global: the old code set
        // the raw pipeline, the fix sets its DI wrapper, and an identity check would fail on that
        // difference alone, bootstrap or not.
        (_, RecordingSink sinkB) = Start();
        AssertArrives(sinkB, "control");

        Start(onBuild: () => Log.ForContext("SourceContext", "HostLoggerGuard").Warning("during-build"));

        Assert.Contains("during-build", sinkB.Guarded);
    }

    [Fact]
    public void TheFirstHost_StillInstallsTheBootstrapLogger()
    {
        // THE PRODUCTION CASE, and it must not change: one host, nothing live before it, and a
        // configuration failure must still reach the console. A bootstrap logger that no longer
        // arrives would leave the sentinel in place. This case passes against the old code too -
        // it guards what the fix must keep, and is seen red by removing the install, not by the bug.
        // It is also red if a host from another test is still live, which is a leak worth seeing.
        var sentinel = new LoggerConfiguration().CreateLogger();
        Log.Logger = sentinel;

        ILogger? duringBuild = null;
        Start(onBuild: () => duringBuild = Log.Logger);

        Assert.NotNull(duringBuild);
        Assert.NotSame(sentinel, duringBuild);
        AssertNotSilent(duringBuild);
    }

    [Fact]
    public void WhenTheLastHostStops_TheGlobalIsWhatItWasBeforeAnyHost()
    {
        // In this assembly that is LogCapture's pipeline, which closing the global used to kill for
        // every test that ran after a host had stopped.
        var before = new LoggerConfiguration().CreateLogger();
        Log.Logger = before;
        (WebApplicationFactory<Program> a, RecordingSink sinkA) = Start();
        AssertArrives(sinkA, "control");

        a.Dispose();

        Assert.Same(before, Log.Logger);
    }

    private (WebApplicationFactory<Program> Host, RecordingSink Sink) Start(Action? onBuild = null)
    {
        var sink = new RecordingSink();
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                onBuild?.Invoke();
                services.AddSingleton<ILogEventSink>(sink);
                // The problem cache lives and dies with this test, not in the user profile.
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _root,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });
        _hosts.Add(host);
        using (host.CreateClient())
        {
        }
        return (host, sink);
    }

    private static void AssertArrives(RecordingSink sink, string text)
    {
        // CALL-SITE, resolved now: the shape that read a silent global after another host stopped.
        Log.ForContext("SourceContext", "HostLoggerGuard").Warning(text);
        Assert.Contains(text, sink.Guarded);
    }

    // By name: Serilog's SilentLogger is internal, which is also how the measurements read it.
    private static void AssertNotSilent(ILogger logger) =>
        Assert.NotEqual("SilentLogger", logger.GetType().Name);

    private static void AssertStillLogging(RecordingSink sink)
    {
        // The old code's closer ran on the stopping host's thread a little after Dispose returned,
        // so the check is made once the global has had time to change.
        Thread.Sleep(TimeSpan.FromMilliseconds(250));
        AssertNotSilent(Log.Logger);
        Assert.False(sink.Disposed, "a live host's pipeline was disposed by another host stopping");
        AssertArrives(sink, "after");
    }

    private sealed class RecordingSink : ILogEventSink, IDisposable
    {
        private readonly List<string> _guarded = [];

        public bool Disposed { get; private set; }

        public IReadOnlyList<string> Guarded
        {
            get
            {
                lock (_guarded)
                {
                    return [.. _guarded];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var context) &&
                context.ToString().Trim('"') == "HostLoggerGuard")
            {
                lock (_guarded)
                {
                    _guarded.Add(logEvent.RenderMessage());
                }
            }
        }

        public void Dispose() => Disposed = true;
    }
}
