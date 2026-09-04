using System.Text.RegularExpressions;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Every integration test that boots the real host replaces the problem store with one on a
/// temp root of its own.
/// </summary>
/// <remarks>
/// <para>
/// The host registers <c>AddProblems(Environment.SpecialFolder.ApplicationData)</c>, so a test
/// that boots it and leaves that registration alone writes its cache into the developer's real
/// user profile - one JSON file naming a temp project folder that the test deletes moments
/// later. Nothing fails; the file simply stays. <see cref="Problems.ProblemStore.KnownRoots"/>
/// reads every one of them and <see cref="Problems.StartupRevalidation"/> walks each at every
/// host start, logging a warning for the folder that is no longer there.
/// </para>
/// <para>
/// That is quadratic in the worst way: 605 accumulated files times roughly 900 host startups in
/// a run came to 22,591 warnings and a suite that looked hung rather than slow. Four classes
/// were leaking; the other twenty already carried the override, and
/// <see cref="DiagramToolboxFlowTests"/>'s comment records the same defect being found and
/// fixed for that one class alone. The fix never generalised because nothing asked it to -
/// which is what this test is: the twenty-third host-booting class cannot reintroduce the leak
/// without turning this red.
/// </para>
/// <para>
/// It reads sources rather than services because the choice being guarded is made inside a
/// <c>ConfigureServices</c> lambda that no assembly metadata exposes; nothing can be reflected
/// over to find it, and a runtime check would only ever prove the one host it booted itself.
/// </para>
/// </remarks>
public class ProblemStoreIsolationTests
{
    private const string ThisFile = "ProblemStoreIsolation.Tests.cs";

    /// <summary>Marks a file as booting the real host - the fixture every flow test takes.</summary>
    private const string BootsTheHost = "WebApplicationFactory<Program>";

    /// <summary>
    /// The removal, however the file qualifies the interface: bare after a using, or through
    /// <c>Problems.</c> as most of these files spell it.
    /// </summary>
    private static readonly Regex ReplacesTheStore = new(
        @"RemoveAll<\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*IProblemStore\s*>",
        RegexOptions.Compiled);

    [Fact]
    public void EveryIntegrationTestThatBootsTheHost_GivesTheProblemStoreATempRootOfItsOwn()
    {
        // Arrange.
        // This file itself is skipped: it names the fixture type and the override in its own
        // text, so it would answer both questions about itself and prove nothing.
        var sources = Directory.EnumerateFiles(IntegrationTestsFolder, "*.cs")
            .Where(source => !IoPath.GetFileName(source).Equals(ThisFile, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var booting = sources.Where(source => File.ReadAllText(source).Contains(BootsTheHost, StringComparison.Ordinal)).ToArray();

        // Assert, first, that the walk found the suite at all. Without this the test passes
        // loudest exactly when it has stopped looking at anything - a renamed folder or a
        // changed fixture type would otherwise turn the guard off in silence.
        Assert.True(
            booting.Length >= 20,
            $"Only {booting.Length} integration test sources mention {BootsTheHost}; this guard has stopped finding the suite it guards.");

        // Act.
        var leaking = booting
            .Where(source => !ReplacesTheStore.IsMatch(File.ReadAllText(source)))
            .Select(IoPath.GetFileName)
            .ToArray();

        // Assert.
        Assert.True(
            leaking.Length == 0,
            "These integration tests boot the real host without replacing IProblemStore, so their "
            + "problem cache is written into the real user profile and re-walked by every later run:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, leaking.Select(name => "  " + name))
            + Environment.NewLine
            + "Copy the RemoveAll<Problems.IProblemStore>() override from DiagramToolboxFlowTests, "
            + "pointing the replacement at a temp root the test deletes when it disposes.");
    }

    /// <summary>
    /// The suite's own source folder, found by walking up from the test binary rather than by
    /// counting `..` segments - the count changes with the build layout, the folder name does
    /// not. The same walk <see cref="ExampleRegistrationTests"/> uses, for the same reason.
    /// </summary>
    private static string IntegrationTestsFolder { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "backend", "EtAlii.Adp.Backend.Tests", "Integration Tests");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The integration tests folder was not found above the test binary.");
    }
}
