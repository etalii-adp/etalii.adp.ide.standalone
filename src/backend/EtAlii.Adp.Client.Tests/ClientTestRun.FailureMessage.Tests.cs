using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Client.Tests;

/// <summary>
/// A client test that fails must reach the .NET output with its own message.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes</b>: on 2026-09-25 a gate went red on one client test, and the .NET
/// output said only <c>Error: STACK_TRACE_ERROR</c>, located at the test's declaration. The test
/// had timed out. vitest's timeout error keeps a stack captured when the test was declared, and the
/// JSON reporter prints a failure's stack in preference to its message, so "Test timed out in ..."
/// never reached the report. <c>failure-message-reporter.mjs</c> puts the message back.
/// </para>
/// <para>
/// <b>The failures are produced on purpose, by a suite of their own</b> in
/// <c>Fixtures/FailingOnPurpose</c>, with its own config. The client suite cannot be made to fail
/// for this without failing the gate; this suite is outside everything the client suite includes.
/// It costs one extra small vitest run.
/// </para>
/// </remarks>
public class ClientTestRunFailureMessageTests
{
    private static readonly Lazy<ClientRunResult> FailingRun = new(() =>
    {
        var fixture = IoPath.Combine(
            ClientTestRun.RepositoryRoot(), "src", "backend", "EtAlii.Adp.Client.Tests", "Fixtures", "FailingOnPurpose");
        var config = IoPath.Combine(fixture, "vitest.config.mjs");
        return ClientTestRun.Drive(ClientTestRun.ClientRoot(), $"--root \"{fixture}\" --config \"{config}\"");
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    [Theory]
    [InlineData("times out on purpose", "Test timed out in 50ms.")]
    [InlineData("fails an assertion on purpose", "expected 1 to be 2")]
    public void AFailedClientTest_ReachesTheOutputWithItsMessage(string fullName, string message)
    {
        // Arrange.
        var run = FailingRun.Value;
        Assert.True(run.Failure is null, run.Failure);

        // Act.
        var test = run.Files.SelectMany(file => file.Tests).FirstOrDefault(test => test.FullName == fullName);

        // Assert.
        Assert.True(test is not null, $"the failing-on-purpose suite reported no \"{fullName}\". It printed:{Environment.NewLine}{run.Output}");
        Assert.Equal("failed", test.Status);
        var output = ClientTests.Describe(test);
        Assert.True(output.Contains(message, StringComparison.Ordinal), $"\"{message}\" did not reach the .NET output, which was:{Environment.NewLine}{output}");
    }
}
