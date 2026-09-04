using Serilog;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// That a <b>module</b> test project can assert on what a <c>private static readonly ILogger</c>
/// wrote. This guards the mechanism, not this module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists, and why here.</b> `LogCapture` is shared into every `*.Tests` project as
/// <em>source</em> by `src/Directory.Build.targets`, and installs its pipeline from a
/// <c>[ModuleInitializer]</c>. That combination is what makes log assertions possible at all
/// here: this repository holds loggers as <c>private static readonly ILogger</c> fields bound at
/// type initialisation, so a sink substituted from a test constructor would arrive too late. The
/// module initializer runs once per <em>assembly</em>, before any static field in that assembly's
/// code under test can bind.
/// </para>
/// <para>
/// <b>The trap this is here to spring.</b> The mechanism works <em>because</em> the source is
/// compiled into each test project. Turning `TestSupport` into a referenced assembly - which
/// reads as tidying, and would compile, and would leave every existing test green - silently
/// breaks it: the initializer would run for the support assembly rather than for the consumer,
/// and a module test project's static loggers would bind to the default <c>Log.Logger</c> and
/// capture nothing. Nothing else in the suite observes that. This test does, and it lives in a
/// module test project because a core one could not tell the two arrangements apart.
/// </para>
/// <para>
/// <b>It logs its own event on purpose.</b> Asserting on some production class's warning would
/// couple this guard to that class's wording, so a legitimate message change would fail it for
/// the wrong reason - a guard over prose. The static field below is the convention itself,
/// standing in for every class that uses it.
/// </para>
/// </remarks>
[Collection(LogCapture.Collection)]
public class LogCaptureReachesModuleTestProjectsTests
{
    /// <summary>Held exactly as CLAUDE.md's logging convention mandates, which is the point.</summary>
    private static readonly ILogger _logger = Log.ForContext<LogCaptureReachesModuleTestProjectsTests>();

    [Fact]
    public void AStaticLoggerInAModuleAssembly_IsCapturedByAModuleTestProject()
    {
        // Arrange.
        const string sentinel = "log capture reaches module test projects";

        // Act.
        using var capture = LogCapture.Start();
        _logger.Warning("A guard says: {Sentinel}", sentinel);

        // Assert.
        // A failure here means the shared-source arrangement in src/Directory.Build.targets has
        // been changed into something that does not install the pipeline for this assembly.
        Assert.Contains(capture.Warnings, warning => warning.Contains(sentinel, StringComparison.Ordinal));
    }
}
