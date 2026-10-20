using EtAlii.Adp.TestSupport.Host;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// That a module test project's log capture still hears a logger bound while one of its flows'
/// hosts is running, and after that host has stopped. This guards the mechanism, not this module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> A running host points <see cref="Log.Logger"/> at its own pipeline, and a
/// <c>private static readonly ILogger</c> binds to whatever the global was when its type was first
/// touched. Since the flows that test one module moved into that module's test project (spec 025,
/// T303), a host runs beside the tests that assert on <c>LogCapture</c>, so a class first used
/// while a flow's host was up kept that host's pipeline and its events never reached the capture.
/// <c>LogCaptureReachesModuleTestProjectsTests</c> failed that way in a full run and passed alone;
/// a presence assertion fails, an absence assertion such as <c>Assert.Empty(capture.Warnings)</c>
/// passes without having looked.
/// </para>
/// <para>
/// <b>Deterministic on purpose.</b> The logger is bound inside the host's lifetime here rather than
/// left to the scheduler, so this fails on every run when the host's pipeline does not hand its
/// events on to the pipeline the capture listens to.
/// </para>
/// </remarks>
[Collection(LogCapture.Collection)]
public class LogCaptureThroughAFlowHostTests
{
    [Fact]
    public void ALoggerBoundWhileAFlowHostRuns_IsCaptured_WhileItRunsAndAfterItStops()
    {
        // Arrange.
        using var capture = LogCapture.Start();
        ILogger logger;

        // Act.
        using (var host = new HostFixture())
        {
            // Reading the services starts the host, which points Log.Logger at its pipeline.
            Assert.NotNull(host.Services);
            logger = Log.ForContext<LogCaptureThroughAFlowHostTests>();
            logger.Warning("A guard says: {Sentinel}", "while the host runs");
        }

        logger.Warning("A guard says: {Sentinel}", "after the host stopped");

        // Assert.
        Assert.Contains(capture.Warnings, warning => warning.Contains("while the host runs", StringComparison.Ordinal));
        Assert.Contains(capture.Warnings, warning => warning.Contains("after the host stopped", StringComparison.Ordinal));
    }

    /// <summary>
    /// The window before the host's pipeline is installed: while the host is being built the global
    /// is the bootstrap logger, and a static logger first touched then keeps it for the process.
    /// <c>LogCaptureReachesModuleTestProjectsTests</c> failed that way in a full run and passed alone.
    /// Deterministic when no other host is live, which is the only case the window exists in.
    /// </summary>
    [Fact]
    public void ALoggerBoundWhileAFlowHostIsBeingBuilt_IsCaptured()
    {
        // Arrange.
        using var capture = LogCapture.Start();
        using var host = new BindingHostFixture();

        // Act.
        Assert.NotNull(host.Services);
        host.BoundWhileBuilding!.Warning("A guard says: {Sentinel}", "bound while the host was being built");

        // Assert.
        Assert.Contains(capture.Warnings, warning => warning.Contains("bound while the host was being built", StringComparison.Ordinal));
    }

    /// <summary>A flow host that binds a logger while its services are being configured, inside the build.</summary>
    private sealed class BindingHostFixture : HostFixture
    {
        public ILogger? BoundWhileBuilding { get; private set; }

        protected override void ConfigureServices(IServiceCollection services) => BoundWhileBuilding = Log.ForContext<BindingHostFixture>();
    }
}
