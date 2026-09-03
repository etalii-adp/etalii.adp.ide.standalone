using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The informational severity, guarded at every point it could quietly disappear.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode this level invites is not a crash: it is a severity that exists in the
/// enum and arrives somewhere downstream as a different one. Two collapse points made that
/// likely - the wire mapping and the client filter each used a two-way ternary that sent
/// everything which was not an Error down the Warning arm - so the guard follows an Info
/// finding from a validator's own record all the way onto the wire.
/// </para>
/// <para>
/// The numbering test is the other half, and it guards a silent data fault rather than a
/// display one: severities persist to the problem cache as NUMBERS, so renumbering Warning or
/// Error would make every cache file written before the change read back one level too low.
/// </para>
/// </remarks>
public class InfoSeverityTests
{
    [Fact]
    public void TheExistingLevels_KeepTheNumbersTheirCachesAlreadyHold()
    {
        // Assert.
        // Pinned, not incidental: ProblemStore serializes severity as a number with no
        // string converter, so these two values are a persisted format. Info sits below
        // them at -1 precisely so adding it moved neither.
        Assert.Equal(0, (int)DiagramProblemSeverity.Warning);
        Assert.Equal(1, (int)DiagramProblemSeverity.Error);
        Assert.Equal(-1, (int)DiagramProblemSeverity.Info);
    }

    [Fact]
    public void InfoOrdersBelowWarning_SoAboveInfoLevelIsExpressible()
    {
        // Assert.
        // Several specs measure their examples as "zero findings above info level", which is
        // this comparison and nothing more elaborate.
        Assert.True(DiagramProblemSeverity.Info < DiagramProblemSeverity.Warning);
        Assert.True(DiagramProblemSeverity.Warning < DiagramProblemSeverity.Error);
    }

    [Fact]
    public void AnInfoFinding_ReachesTheWireAsAnInfo()
    {
        // Arrange.
        // The whole path a finding takes: a validator's DiagramProblem, stored, then mapped
        // for the client. The mapping is where a two-way ternary used to lose it.
        var stored = new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Info, "Described elsewhere or missing.", "shacl.described-elsewhere"),
            RelativePath: "shapes.ttl",
            LastWriteTimeUtc: DateTime.UtcNow,
            Length: 42,
            RulesVersion: "test",
            Stale: false);
        var set = new ProjectProblemSet(ProjectProblemSetState.Validated, [stored], ErrorCount: 0, WarningCount: 0, TruncatedAt: 0, InfoCount: 1);

        // Act.
        var proto = ProblemBroadcaster.ToProto(set);

        // Assert.
        var problem = Assert.Single(proto.Problems);
        Assert.Equal(ProblemSeverity.Info, problem.Severity);
        Assert.Equal("shacl.described-elsewhere", problem.RuleId);
    }

    [Fact]
    public void EachSeverity_MapsToItsOwnWireValue_AndNoneCollapses()
    {
        // Arrange.
        var stored = new[]
        {
            Problem(DiagramProblemSeverity.Info, "info"),
            Problem(DiagramProblemSeverity.Warning, "warning"),
            Problem(DiagramProblemSeverity.Error, "error"),
        };
        var set = new ProjectProblemSet(
            ProjectProblemSetState.Validated, stored, ErrorCount: 1, WarningCount: 1, TruncatedAt: 0, InfoCount: 1);

        // Act.
        var proto = ProblemBroadcaster.ToProto(set);

        // Assert.
        // Three in, three distinct out: the property that the old ternary could not have.
        Assert.Equal(
            [ProblemSeverity.Info, ProblemSeverity.Warning, ProblemSeverity.Error],
            proto.Problems.Select(problem => problem.Severity).ToArray());
    }

    [Fact]
    public void TheInfoCount_TravelsSeparately_AndIsNeverFoldedIntoTheWarnings()
    {
        // Arrange.
        // A file whose only findings are informational must not report as having warnings -
        // that is the difference between "here is a note" and "something is wrong".
        var set = new ProjectProblemSet(
            ProjectProblemSetState.Validated,
            [Problem(DiagramProblemSeverity.Info, "note")],
            ErrorCount: 0,
            WarningCount: 0,
            TruncatedAt: 0,
            InfoCount: 1);

        // Act.
        var proto = ProblemBroadcaster.ToProto(set);

        // Assert.
        Assert.Equal(1u, proto.InfoCount);
        Assert.Equal(0u, proto.WarningCount);
        Assert.Equal(0u, proto.ErrorCount);
    }

    private static StoredProblem Problem(DiagramProblemSeverity severity, string message) =>
        new(
            new DiagramProblem(severity, message, $"test.{message}"),
            RelativePath: "file.adp",
            LastWriteTimeUtc: DateTime.UtcNow,
            Length: 1,
            RulesVersion: "test",
            Stale: false);
}
