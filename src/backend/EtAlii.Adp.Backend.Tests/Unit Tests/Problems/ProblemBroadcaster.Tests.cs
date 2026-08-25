using System.Threading.Channels;

using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProblemBroadcasterTests
{
    private const string Root = @"C:\root";

    [Fact]
    public void AStoreChange_BecomesOnePushToThatProject()
    {
        var problems = new ProblemBroadcasterStubProblemStore();
        var selections = new ProblemBroadcasterRecordingSelectionStore();
        using var broadcaster = new ProblemBroadcaster(problems, selections);

        problems.RaiseChanged(Root);

        var push = Assert.Single(selections.Pushes);
        Assert.Equal(Root, push.RootPath);
        Assert.Equal(ProblemSetState.Validated, push.Problems.State);
    }

    [Fact]
    public void Disposed_ItPushesNothing()
    {
        var problems = new ProblemBroadcasterStubProblemStore();
        var selections = new ProblemBroadcasterRecordingSelectionStore();
        var broadcaster = new ProblemBroadcaster(problems, selections);
        broadcaster.Dispose();

        problems.RaiseChanged(Root);

        Assert.Empty(selections.Pushes);
    }

    [Fact]
    public void ToProto_CarriesEverythingTheWireNeeds()
    {
        var stored = new StoredProblem(
            new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "The root is lonely.",
                "mindmap.lonely-root",
                new DiagramProblemElementLocation("node-1")),
            IoPath.Combine("folder", "flow.adp"),
            DateTime.UtcNow,
            42,
            "1.0.0",
            Stale: true);
        var set = new ProjectProblemSet(ProjectProblemSetState.Validated, [stored], ErrorCount: 3, WarningCount: 2, TruncatedAt: 1);

        var proto = ProblemBroadcaster.ToProto(set);

        Assert.Equal(ProblemSetState.Validated, proto.State);
        Assert.Equal(3u, proto.ErrorCount);
        Assert.Equal(2u, proto.WarningCount);
        Assert.Equal(1u, proto.TruncatedAt);
        var problem = Assert.Single(proto.Problems);
        Assert.Equal(ProblemSeverity.Warning, problem.Severity);
        Assert.Equal("The root is lonely.", problem.Message);
        Assert.Equal("mindmap.lonely-root", problem.RuleId);
        Assert.True(problem.Stale);
        Assert.Equal(["folder", "flow.adp"], problem.Path.Segments);
        Assert.Equal("node-1", problem.Location.ElementId.Value);
    }

    [Fact]
    public void ToProto_ALineLocation_AndNoLocation_BothSurvive()
    {
        var line = new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Error, "Bad line.", "x.y", new DiagramProblemLineLocation(7)),
            "a.adp", DateTime.UtcNow, 1, "");
        var file = new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Error, "Bad file.", "x.y"),
            "b.adp", DateTime.UtcNow, 1, "");

        var proto = ProblemBroadcaster.ToProto(new ProjectProblemSet(ProjectProblemSetState.Validated, [line, file], 2, 0, 0));

        Assert.Equal(7u, proto.Problems[0].Location.Line);
        Assert.Null(proto.Problems[1].Location);
    }

    // ---- plumbing ----------------------------------------------------------------------

}
