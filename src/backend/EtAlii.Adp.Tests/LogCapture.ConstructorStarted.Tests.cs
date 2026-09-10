using Serilog;
using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// A capture started while the test class is being CONSTRUCTED still collects what the test
/// method logs.
/// </summary>
/// <remarks>
/// Two users start their capture in a field initializer, so the capture begins before the
/// test method does. A capture scoped to the test that started it has to recognise that the
/// constructor and the method belong to the same test; if it did not, those captures would
/// see nothing at all, their presence assertions would fail loudly and their absence
/// assertions would pass for the worst reason. This pins the assumption the scoping rests on
/// rather than leaving it to be discovered in someone else's project.
/// </remarks>
public class LogCaptureConstructorStartedTests : IDisposable
{
    private readonly LogCapture _capture = LogCapture.Start();

    public void Dispose() => _capture.Dispose();

    [Fact]
    public void ACaptureStartedInTheConstructor_CollectsWhatTheTestMethodLogs()
    {
        // Arrange.
        var sentinel = Guid.NewGuid().ToString("N");

        // Act.
        Log.ForContext("SourceContext", "The.Subject").Warning("Logged by the test method {Sentinel}", sentinel);

        // Assert.
        Assert.Contains(_capture.Warnings, warning => warning.Contains(sentinel, StringComparison.Ordinal));
    }
}
