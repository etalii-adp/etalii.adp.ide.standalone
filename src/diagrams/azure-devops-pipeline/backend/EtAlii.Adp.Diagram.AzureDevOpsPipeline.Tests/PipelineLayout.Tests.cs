using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The layout, which exists because an Azure pipeline has no coordinates for anyone to have
/// chosen. Most of these assert on layers and rows rather than on pixels: "third column, second
/// row" is the thing the layout promises, and arithmetic against a pitch would just restate the
/// implementation back at itself.
/// </summary>
public class PipelineLayoutTests
{
    /// <summary>A pitch of round numbers, so the few tests that do check pixels stay readable.</summary>
    private static readonly PipelineMetrics _metrics = new(
        StageWidth: 100,
        StageHeight: 50,
        JobWidth: 60,
        JobHeight: 20,
        HorizontalGap: 10,
        VerticalGap: 5,
        Padding: 10,
        HeaderHeight: 20);

    private static PipelineModel ParseFixture(string name) =>
        PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static PipelineModel Parse(string text) => PipelineParser.Parse(LineDocument.Parse(text));

    private static PipelineArrangement ArrangeStages(PipelineModel model) =>
        PipelineLayout.Arrange(PipelineGraphBuilder.OfStages(model), _metrics);

    /// <summary>A pipeline of named stages, each depending on what the caller says.</summary>
    private static PipelineModel Pipeline(params string[] stages)
    {
        var text = "stages:\n" + string.Concat(stages.Select(stage =>
            $"  - stage: {stage.Split(':')[0]}\n" +
            (stage.Contains(':', StringComparison.Ordinal) && stage.Split(':')[1].Length > 0
                ? $"    dependsOn: [{stage.Split(':')[1]}]\n"
                : stage.Contains(':', StringComparison.Ordinal)
                    ? "    dependsOn: []\n"
                    : "") +
            "    jobs:\n      - job: J\n        steps:\n          - script: x\n"));
        return Parse(text);
    }

    [Fact]
    public void ASequentialPipeline_RunsLeftToRightOneStagePerColumn()
    {
        // Arrange & act.
        var arrangement = ArrangeStages(Pipeline("A", "B", "C"));

        // Assert.
        Assert.Equal([0, 1, 2], arrangement.Placements.Select(placement => placement.Layer));
        Assert.Equal([0, 0, 0], arrangement.Placements.Select(placement => placement.Row));
    }

    [Fact]
    public void StagesThatCanRunAtTheSameTime_ShareAColumn()
    {
        // Arrange: the thing the layered arrangement is for. B and C both wait for A only, so
        // nothing stops them running together and they belong side by side.
        var model = Pipeline("A:", "B:A", "C:A");

        // Act.
        var arrangement = ArrangeStages(model);

        // Assert.
        Assert.Equal(0, arrangement.Of("A")!.Layer);
        Assert.Equal(1, arrangement.Of("B")!.Layer);
        Assert.Equal(1, arrangement.Of("C")!.Layer);
        Assert.Equal(0, arrangement.Of("B")!.Row);
        Assert.Equal(1, arrangement.Of("C")!.Row);
    }

    [Fact]
    public void ALayerIsTheLongestDepth_NotTheShortest()
    {
        // Arrange: D waits for both A and C, and C waits for B waits for A. With shortest depth D
        // would sit in column one, with an arrow coming backwards into it from column three.
        var model = Pipeline("A:", "B:A", "C:B", "D:A, C");

        // Act.
        var arrangement = ArrangeStages(model);

        // Assert.
        Assert.Equal(3, arrangement.Of("D")!.Layer);
    }

    public static TheoryData<string> GraphShapes()
    {
        var shapes = new TheoryData<string>();
        foreach (var path in Directory.GetFiles("Fixtures", "*.yml", SearchOption.AllDirectories))
        {
            shapes.Add(File.ReadAllText(path));
        }

        // The corpus is real pipelines, and none of them happens to contain a diamond - where a
        // stage waits for both an early one and a late one. That is the only shape on which
        // longest and shortest depth disagree, so without it this property passes just as happily
        // against a layout that gets the rule wrong.
        shapes.Add("""
            stages:
              - stage: A
                dependsOn: []
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: B
                dependsOn: [A]
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: C
                dependsOn: [B]
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: D
                dependsOn: [A, C]
                jobs:
                  - job: J
                    steps:
                      - script: x
            """);
        return shapes;
    }

    [Theory]
    [MemberData(nameof(GraphShapes))]
    public void EveryArrow_PointsForward(string yaml)
    {
        // Arrange: the property the layering exists to produce.
        var model = PipelineParser.Parse(LineDocument.Parse(yaml));
        var graph = PipelineGraphBuilder.OfStages(model);
        if (graph.Cycles.Count > 0)
        {
            // A cycle has no forward direction; the graph reports it and the canvas draws it.
            return;
        }

        // Act.
        var arrangement = PipelineLayout.Arrange(graph, _metrics);

        // Assert.
        foreach (var edge in graph.Edges.Where(edge => !edge.IsBroken))
        {
            Assert.True(
                arrangement.Of(edge.FromId)!.Layer < arrangement.Of(edge.ToId)!.Layer,
                $"{edge.FromId} -> {edge.ToId} does not point forward");
        }
    }

    [Fact]
    public void TheSameFile_AlwaysProducesTheSamePicture()
    {
        // Arrange: Requirement 7.2 - determinism, which a layout leaning on hash ordering
        // anywhere would quietly lose.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var first = ArrangeStages(model);
        var second = ArrangeStages(PipelineParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine("Fixtures", "multi-stage.yml")))));

        // Assert.
        Assert.Equal(
            first.Placements.Select(placement => (placement.Id, placement.X, placement.Y)),
            second.Placements.Select(placement => (placement.Id, placement.X, placement.Y)));
    }

    [Fact]
    public void RenamingAStep_MovesNothing()
    {
        // Arrange: an edit that does not touch the graph must not rearrange the diagram, or every
        // rename becomes a diff of the whole picture (Requirement 7.2).
        var document = LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", "multi-stage.yml")));
        var before = ArrangeStages(PipelineParser.Parse(document));

        // Act.
        var step = PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Compile").Steps[1];
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(step), "Renamed entirely");

        // Assert.
        var after = ArrangeStages(PipelineParser.Parse(document));
        Assert.Equal(
            before.Placements.Select(placement => (placement.Id, placement.X, placement.Y)),
            after.Placements.Select(placement => (placement.Id, placement.X, placement.Y)));
    }

    [Fact]
    public void ColumnsAreSpacedByTheirWidthAndTheGap()
    {
        // Arrange & act.
        var arrangement = ArrangeStages(Pipeline("A", "B", "C"));

        // Assert.
        Assert.Equal(0, arrangement.Of("A")!.X);
        Assert.Equal(110, arrangement.Of("B")!.X);
        Assert.Equal(220, arrangement.Of("C")!.X);
    }

    [Fact]
    public void RowsAreSpacedByTheirHeightAndTheGap()
    {
        // Arrange & act.
        var arrangement = ArrangeStages(Pipeline("A:", "B:A", "C:A"));

        // Assert.
        Assert.Equal(0, arrangement.Of("B")!.Y);
        Assert.Equal(55, arrangement.Of("C")!.Y);
    }

    [Fact]
    public void ACycle_IsLaidOutRatherThanHangingTheLayout()
    {
        // Arrange: a cycle has no longest path, and asking for one does not terminate. A pipeline
        // with a mistake in it still has to produce the picture that shows the mistake.
        var model = ParseFixture("edge-broken-graph.yml");

        // Act.
        var arrangement = ArrangeStages(model);

        // Assert.
        Assert.Equal(model.Stages.Count, arrangement.Placements.Count);
        Assert.All(arrangement.Placements, placement => Assert.InRange(placement.Layer, 0, model.Stages.Count));
    }

    [Fact]
    public void ABrokenEdge_DoesNotDragItsStageIntoAColumnOfItsOwn()
    {
        // Arrange: the name matches nothing, so there is nothing for Ghost to sit after.
        var model = ParseFixture("edge-broken-graph.yml");

        // Act.
        var arrangement = ArrangeStages(model);

        // Assert.
        Assert.Equal(0, arrangement.Of("Ghost")!.Layer);
    }

    [Fact]
    public void AnEmptyPipeline_ArrangesToNothing()
    {
        // Arrange & act.
        var arrangement = PipelineLayout.Arrange(PipelineGraph.Empty, _metrics);

        // Assert.
        Assert.Same(PipelineArrangement.Empty, arrangement);
    }

    [Fact]
    public void AnExpandedStage_HoldsItsJobsInside()
    {
        // Arrange: Requirement 7.5 - the same rule at both levels, so a reader who has learned to
        // read the stage graph can read the job graph.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "Test" });

        // Assert.
        var stage = arrangement.Of("Test")!;
        foreach (var job in new[] { "Test/Unit", "Test/Integration" })
        {
            var placed = arrangement.Of(job)!;
            Assert.InRange(placed.X, stage.X, stage.X + stage.Size.Width - placed.Size.Width);
            Assert.InRange(placed.Y, stage.Y, stage.Y + stage.Size.Height - placed.Size.Height);
        }
    }

    [Fact]
    public void AnExpandedStage_GrowsToFitItsJobs()
    {
        // Arrange: Unit and Integration run in parallel, so they stack and the stage has to be
        // taller than a collapsed one.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "Test" });

        // Assert.
        var stage = arrangement.Of("Test")!;
        Assert.True(stage.Size.Height > _metrics.StageHeight, "an expanded stage must not be collapsed-sized");
        Assert.Equal(_metrics.StageHeight, arrangement.Of("Build")!.Size.Height);
    }

    [Fact]
    public void JobsInsideAStage_FollowTheirOwnDependencyGraph()
    {
        // Arrange: jobs default to parallel, so these two share a column inside the stage.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "Test" });

        // Assert.
        Assert.Equal(arrangement.Of("Test/Unit")!.X, arrangement.Of("Test/Integration")!.X);
        Assert.NotEqual(arrangement.Of("Test/Unit")!.Y, arrangement.Of("Test/Integration")!.Y);
    }

    [Fact]
    public void JobsThatWaitForEachOther_SitInDifferentColumnsInsideTheStage()
    {
        // Arrange.
        var model = ParseFixture("jobs-only.yml");

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "stage-0" });

        // Assert.
        Assert.True(arrangement.Of("stage-0/Test")!.X > arrangement.Of("stage-0/Build")!.X);
    }

    [Fact]
    public void ACollapsedPipeline_PlacesOnlyItsStages()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics);

        // Assert.
        Assert.Equal(model.Stages.Count, arrangement.Placements.Count);
        Assert.Null(arrangement.Of("Test/Unit"));
    }

    [Fact]
    public void AStageBesideAnExpandedOne_DoesNotOverlapIt()
    {
        // Arrange: a column is as wide as its widest element, so an expanded stage pushes the
        // next column across rather than being drawn over.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "Test" });

        // Assert.
        var test = arrangement.Of("Test")!;
        var staging = arrangement.Of("DeployStaging")!;
        Assert.True(staging.X >= test.X + test.Size.Width, "the next column starts after the expanded stage ends");
    }

    [Fact]
    public void ALargeStage_LaysOutWithoutComplaint()
    {
        // Arrange: the schema permits 256 jobs in a stage (Requirement 7.7). They all depend on
        // nothing, so they are one very tall column - readable, and nothing to untangle by hand.
        var jobs = string.Concat(Enumerable.Range(0, 256).Select(index =>
            $"  - job: J{index}\n    steps:\n      - script: x\n"));
        var model = Parse("jobs:\n" + jobs);

        // Act.
        var arrangement = PipelineLayout.ArrangePipeline(model, _metrics, new HashSet<string> { "stage-0" });

        // Assert.
        Assert.Equal(257, arrangement.Placements.Count);
        Assert.Equal(256, arrangement.Placements.Count(placement => placement.Id.StartsWith("stage-0/", StringComparison.Ordinal)));
        Assert.Equal(255, arrangement.Of("stage-0/J255")!.Row);
    }

    [Fact]
    public void ADeepPipeline_DoesNotOverflowTheStack()
    {
        // Arrange: the longest path is found by recursion, so a long chain is the shape that
        // would find its limit.
        var stages = string.Concat(Enumerable.Range(0, 500).Select(index =>
            $"  - stage: S{index}\n    jobs:\n      - job: J\n        steps:\n          - script: x\n"));
        var model = Parse("stages:\n" + stages);

        // Act.
        var arrangement = ArrangeStages(model);

        // Assert.
        Assert.Equal(499, arrangement.Of("S499")!.Layer);
    }
}
