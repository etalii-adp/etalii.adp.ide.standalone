using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The mapper puts a pipeline on the wire in the core's own element vocabulary - this is the
/// fourth diagram type to ride it, which is the point. What is worth holding here is that ids are
/// document paths, so a selection survives an edit somewhere else in the file, and that the things
/// a reader most needs to be told apart - an implicit edge, a broken one, a job that becomes many -
/// arrive marked rather than flattened.
/// </summary>
public class PipelineElementMapperTests
{
    private static readonly PipelineElementMapper _mapper = new(PipelineMetrics.Default);

    private static PipelineModel ParseFixture(string name) =>
        PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static PipelineModel Parse(string text) => PipelineParser.Parse(LineDocument.Parse(text));

    private static PipelineElementPayload PayloadOf(DiagramElement element) =>
        PipelineElementPayload.Parser.ParseFrom(element.Payload.ToArray());

    private static IReadOnlyList<DiagramElement> Map(PipelineModel model, params string[] expanded) =>
        _mapper.Visible(model, DiagramViewport.Unbounded, new HashSet<string>(expanded, StringComparer.Ordinal));

    [Fact]
    public void AStage_TravelsAsOneElementWithItsOwnMimeType()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        var build = elements.Single(element => element.Id == "Build");
        Assert.Equal(PipelineElementMapper.StageType, build.Type);
        Assert.StartsWith("azure-devops/pipeline", build.Type, StringComparison.Ordinal);
        Assert.Equal("Build the solution", PayloadOf(build).DisplayName);
    }

    [Fact]
    public void EveryElement_CarriesTheModulesPayloadTypeUrl()
    {
        // Arrange: the payload rides in the core Any, so the type URL is what lets the client know
        // which module's message it is looking at.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        Assert.All(elements, element => Assert.Equal(PipelineElementMapper.PayloadTypeUrl, element.PayloadTypeUrl));
        Assert.Contains("PipelineElementPayload", PipelineElementMapper.PayloadTypeUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElementId_IsItsPathWithinTheDocument()
    {
        // Arrange: Requirement 11.3. The pipeline schema gives elements names but no ids, and this
        // module will not add any to the file, so the path is what there is.
        var elements = Map(ParseFixture("multi-stage.yml"), "Test");

        // Assert.
        Assert.Contains(elements, element => element.Id == "Test");
        Assert.Contains(elements, element => element.Id == "Test/Unit");
    }

    [Fact]
    public void AnEditElsewhereInTheFile_DoesNotChangeAnElementsId()
    {
        // Arrange: the reason ids are paths - a selection must not be lost on every keystroke.
        var document = LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", "multi-stage.yml")));
        var before = Map(PipelineParser.Parse(document)).Select(element => element.Id).ToList();

        // Act.
        var step = PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Compile").Steps[1];
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(step), "Something else entirely");

        // Assert.
        Assert.Equal(before, Map(PipelineParser.Parse(document)).Select(element => element.Id));
    }

    [Fact]
    public void EveryElementId_IsUnique()
    {
        // Arrange: an id addresses a delta, so a collision would move the wrong element.
        var fixtures = Directory.GetFiles(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures"), "*.yml", SearchOption.AllDirectories);

        // Assert, first, that the walk found the corpus at all: 14 fixtures ship today, so
        // ten is a floor with headroom. Without this the test passes loudest exactly when it
        // has stopped looking at anything - and it used to enumerate a relative "Fixtures",
        // which resolves against the working directory rather than the test binary.
        Assert.True(
            fixtures.Length >= 10,
            $"Only {fixtures.Length} pipeline fixtures were found; this guard has stopped finding the corpus it maps.");

        var mapped = 0;
        foreach (var path in fixtures)
        {
            var model = PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(path)));
            var expanded = model.Stages.Select(stage => stage.Id).ToArray();

            // Act.
            var ids = Map(model, expanded).Select(element => element.Id).ToList();

            // Assert.
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            mapped += ids.Count;
        }

        // And a floor on the second collection, because the assertion above reads 0 == 0 on a
        // fixture that mapped nothing: a mapper returning an empty list for every file passes
        // this test as loudly as a correct one does. The corpus maps 64 elements today, so
        // forty is a floor with real headroom on it.
        Assert.True(
            mapped >= 40,
            $"Only {mapped} elements were mapped across the whole corpus; this guard has stopped finding the elements whose ids it compares.");
    }

    [Fact]
    public void AnElementsPosition_ComesFromTheLayout()
    {
        // Arrange: Requirement 7.3 - the backend answers the viewport query, so it is the backend
        // that has to know where things are.
        var model = ParseFixture("multi-stage.yml");
        var arrangement = PipelineLayout.ArrangePipeline(model, PipelineMetrics.Default);

        // Act.
        var elements = Map(model);

        // Assert.
        var build = elements.Single(element => element.Id == "Build");
        Assert.Equal(arrangement.Of("Build")!.X, build.X);
        Assert.Equal(arrangement.Of("Build")!.Y, build.Y);
    }

    [Fact]
    public void ADependencyEdge_IsAnElementNamingItsTwoEnds()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        var edge = elements.Single(element =>
            element.Type == PipelineElementMapper.EdgeType &&
            PayloadOf(element).TargetId == "Test");
        var payload = PayloadOf(edge);
        Assert.Equal("Build", payload.SourceId);
        Assert.Equal("Test", payload.TargetId);
    }

    [Fact]
    public void AnEdgeId_IsBuiltFromItsEndsRatherThanACounter()
    {
        // Arrange: a counter would renumber every arrow the moment one was added, which is the
        // same problem ids-as-paths solves for boxes.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var elements = Map(model);

        // Assert.
        Assert.Contains(elements, element => element.Id == "edge:Build->Test");
    }

    [Fact]
    public void AnImplicitEdge_ArrivesMarkedAsImplicit()
    {
        // Arrange: a stage with no dependsOn waits for the one before it. The arrow is real, and
        // the fact that nobody wrote it down is what the reader most needs telling.
        var model = Parse("""
            stages:
              - stage: One
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: Two
                jobs:
                  - job: J
                    steps:
                      - script: x
            """);

        // Act.
        var edge = Map(model).Single(element => element.Type == PipelineElementMapper.EdgeType);

        // Assert.
        Assert.True(PayloadOf(edge).ImplicitDependency);
    }

    [Fact]
    public void AnEdgeSomebodyWroteDown_IsNotMarkedImplicit()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        var edge = elements.Single(element => element.Id == "edge:Build->Test");
        Assert.False(PayloadOf(edge).ImplicitDependency);
    }

    [Fact]
    public void ABrokenEdge_ArrivesMarkedBrokenRatherThanMissing()
    {
        // Arrange: a dangling dependency is what this diagram is for (Requirement 6.5).
        var elements = Map(ParseFixture("edge-broken-graph.yml"));

        // Act.
        var broken = elements.Where(element =>
            element.Type == PipelineElementMapper.EdgeType && PayloadOf(element).Broken).ToList();

        // Assert.
        var edge = Assert.Single(broken);
        Assert.Equal("DoesNotExist", PayloadOf(edge).Name);
        Assert.Equal("Ghost", PayloadOf(edge).TargetId);
    }

    [Fact]
    public void TwoDanglingNamesIntoOneStage_AreTwoArrows()
    {
        // Arrange: two different mistakes, and an id built only from the target would collapse
        // them into one.
        var model = Parse("stages:\n  - stage: Ghost\n    dependsOn: [Nope, AlsoNope]\n    jobs:\n      - job: J\n        steps:\n          - script: x\n");

        // Act.
        var edges = Map(model).Where(element => element.Type == PipelineElementMapper.EdgeType).ToList();

        // Assert.
        Assert.Equal(2, edges.Count);
        Assert.Equal(2, edges.Select(edge => edge.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AnEdgesCondition_SaysWhichOutcomeItWaitsFor()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        var edge = elements.First(element =>
            element.Type == PipelineElementMapper.EdgeType && PayloadOf(element).TargetId == "Notify");
        Assert.Equal(PipelineEdgeConditionProto.PipelineEdgeConditionAlways, PayloadOf(edge).EdgeCondition);
        Assert.Equal("always()", PayloadOf(edge).Condition);
    }

    [Fact]
    public void AnUnconditionalEdge_UsesTheProtosZeroValue()
    {
        // Arrange: on-success is the schema's default, so an unset field already means the right
        // thing and no pipeline pays for the commonest case.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        var edge = elements.Single(element => element.Id == "edge:Build->Test");
        Assert.Equal(PipelineEdgeConditionProto.PipelineEdgeConditionOnSuccess, PayloadOf(edge).EdgeCondition);
        Assert.Equal(0, (int)PayloadOf(edge).EdgeCondition);
    }

    [Fact]
    public void ACollapsedStage_SendsNoJobs()
    {
        // Arrange: collapsing changes what this connection is looking at, not what the pipeline
        // contains - which is what Group/Ungroup says, so the jobs are absent rather than marked.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Assert.
        Assert.DoesNotContain(elements, element => element.Type == PipelineElementMapper.JobType);
    }

    [Fact]
    public void AnExpandedStage_SendsItsJobsAndTheirEdges()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("jobs-only.yml"), "stage-0");

        // Assert.
        Assert.Equal(2, elements.Count(element => element.Type == PipelineElementMapper.JobType));
        Assert.Contains(elements, element => element.Id == "edge:stage-0/Build->stage-0/Test");
    }

    [Fact]
    public void AJobKnowsWhichStageItIsIn()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"), "Test");

        // Assert.
        Assert.Equal("Test", PayloadOf(elements.Single(element => element.Id == "Test/Unit")).ParentId);
    }

    [Fact]
    public void ADeploymentJob_IsADifferentKindAndCarriesItsEnvironment()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"), "DeployStaging");

        // Assert.
        var payload = PayloadOf(elements.Single(element => element.Id == "DeployStaging/Staging"));
        Assert.Equal(PipelineElementKindProto.PipelineElementKindDeploymentJob, payload.Kind);
        Assert.Equal("staging", payload.Environment);
        Assert.Equal("RunOnce", payload.Strategy);
    }

    [Fact]
    public void APlainJob_IsThePlainKind()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("multi-stage.yml"), "Test");

        // Assert.
        Assert.Equal(
            PipelineElementKindProto.PipelineElementKindJob,
            PayloadOf(elements.Single(element => element.Id == "Test/Unit")).Kind);
    }

    [Fact]
    public void AMatrixJob_CarriesItsMultiplicity()
    {
        // Arrange: Requirement 4.6 - a job that becomes three at run time must not look like one.
        var model = Parse("""
            jobs:
              - job: Test
                strategy:
                  matrix:
                    linux:
                      image: ubuntu-latest
                    windows:
                      image: windows-latest
                steps:
                  - script: x
            """);

        // Act.
        var elements = Map(model, "stage-0");

        // Assert.
        var payload = PayloadOf(elements.Single(element => element.Id == "stage-0/Test"));
        Assert.Equal(2, payload.Multiplicity);
        Assert.False(payload.Indeterminate);
    }

    [Fact]
    public void AJobMultipliedByAnExpression_IsMarkedIndeterminate()
    {
        // Arrange: nobody knows how many until the run starts, so it is drawn as uncertain rather
        // than drawn as one.
        var model = Parse("jobs:\n  - job: Test\n    strategy:\n      parallel: $(slices)\n    steps:\n      - script: x\n");

        // Act.
        var payload = PayloadOf(Map(model, "stage-0").Single(element => element.Id == "stage-0/Test"));

        // Assert.
        Assert.True(payload.Indeterminate);
    }

    [Fact]
    public void AStageInsideACompileTimeConditional_IsMarkedIndeterminate()
    {
        // Arrange & act.
        var elements = Map(ParseFixture("edge-expressions.yml"));

        // Assert.
        Assert.True(PayloadOf(elements.Single(element => element.Id == "Deploy")).Indeterminate);
        Assert.False(PayloadOf(elements.Single(element => element.Id == "Build")).Indeterminate);
    }

    [Fact]
    public void ADisabledStep_ArrivesDisabled()
    {
        // Arrange & act.
        var model = Parse("steps:\n  - script: x\n    enabled: false\n  - script: y\n");
        var job = model.Jobs.Single();

        // Act.
        var steps = _mapper.StepsOf(job, 0, 0);

        // Assert.
        Assert.False(PayloadOf(steps[0]).Enabled);
        Assert.True(PayloadOf(steps[1]).Enabled);
    }

    [Fact]
    public void AStepGatedOnAnExpression_IsNotDrawnAsSkipped()
    {
        // Arrange: drawing a step as skipped when it in fact runs is the worse mistake of the two.
        var model = Parse("steps:\n  - script: x\n    enabled: $(runIt)\n");

        // Act.
        var step = _mapper.StepsOf(model.Jobs.Single(), 0, 0).Single();

        // Assert.
        Assert.True(PayloadOf(step).Enabled);
    }

    [Fact]
    public void AStepsInDeclaredOrder_KeepIt()
    {
        // Arrange: steps are a sequence and not a graph (Requirement 6.4).
        var model = ParseFixture("multi-stage.yml");
        var compile = model.Jobs.Single(job => job.Name == "Compile");

        // Act.
        var steps = _mapper.StepsOf(compile, 0, 0);

        // Assert.
        // checkout and publish carry no displayName, so each falls back to its own identifying
        // value; the script in the middle has one.
        Assert.Equal(
            ["self", "Build", "$(Build.ArtifactStagingDirectory)"],
            steps.Select(step => PayloadOf(step).DisplayName));
        Assert.Equal(steps.Select(step => step.Y).Order(), steps.Select(step => step.Y));
    }

    [Fact]
    public void AnElementFromATemplate_SaysSoAndSaysWhichOne()
    {
        // Arrange: Requirement 5.4 - the reader has to be able to tell authored-here from
        // included-from-there, because one of them is editable and the other is not.
        var stage = ParseFixture("multi-stage.yml").Stages[0] with { Template = "templates/build.yml" };
        var model = new PipelineModel([stage], [], null, []);

        // Act.
        var payload = PayloadOf(Map(model).Single(element => element.Id == stage.Id));

        // Assert.
        Assert.True(payload.FromTemplate);
        Assert.Equal("templates/build.yml", payload.TemplatePath);
    }

    [Fact]
    public void ATemplateThatCouldNotBeFollowed_GetsAnElementSayingWhy()
    {
        // Arrange: a diagram that quietly drops a template is worse than one that admits the gap.
        var model = Parse("stages:\n  - template: elsewhere.yml@other\n");
        var unresolved = new PipelineModel(
            model.Stages,
            model.Templates,
            null,
            [new PipelineTemplateUnresolved(model.Templates[0], PipelineTemplateUnresolvedReason.OtherRepository)]);

        // Act.
        var element = Map(unresolved).Single(candidate => candidate.Type == PipelineElementMapper.TemplateType);

        // Assert.
        var payload = PayloadOf(element);
        Assert.Equal("elsewhere.yml@other", payload.Reference);
        Assert.Contains("not checked out here", payload.UnresolvedReason);
        Assert.True(payload.Indeterminate);
    }

    [Fact]
    public void ATemplateThatWasFollowed_NeedsNoElementOfItsOwn()
    {
        // Arrange: what it brought is already on the canvas, each piece marked with where it came
        // from - a second box saying "and this came from a template" would be noise.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var elements = Map(model);

        // Assert.
        Assert.DoesNotContain(elements, element => element.Type == PipelineElementMapper.TemplateType);
    }

    [Fact]
    public void AnElementCarriesTheLinesItLivesOn()
    {
        // Arrange: so a double-click can open the file where the element actually is. 1-based,
        // because that is how an editor counts.
        var elements = Map(ParseFixture("multi-stage.yml"));

        // Act.
        var payload = PayloadOf(elements.Single(element => element.Id == "Build"));

        // Assert.
        Assert.True(payload.FirstLine >= 1);
        Assert.True(payload.LastLine >= payload.FirstLine);
    }

    [Fact]
    public void AnImplicitStage_CarriesNoLinesBecauseItHasNone()
    {
        // Arrange: it is not in the file, so there is nothing to open.
        var elements = Map(ParseFixture("jobs-only.yml"));

        // Act.
        var payload = PayloadOf(elements.Single(element => element.Id == "stage-0"));

        // Assert.
        Assert.Equal(0, payload.FirstLine);
        Assert.Equal(0, payload.LastLine);
    }

    [Fact]
    public void AnInheritedPool_SaysItWasInherited()
    {
        // Arrange: "ubuntu-latest, from the pipeline" and "ubuntu-latest, set here" are different
        // facts, and only one of them means this element is where you go to change it.
        var elements = Map(ParseFixture("jobs-only.yml"), "stage-0");

        // Act.
        var payload = PayloadOf(elements.Single(element => element.Id == "stage-0/Build"));

        // Assert.
        Assert.Equal("ubuntu-latest", payload.Pool);
        Assert.True(payload.PoolInherited);
    }

    [Fact]
    public void EveryFixture_MapsWithoutThrowing()
    {
        // Arrange: including the deliberately broken one and the one that is mostly a template.
        var fixtures = Directory.GetFiles(
            IoPath.Combine(AppContext.BaseDirectory, "Fixtures"), "*.yml", SearchOption.AllDirectories);

        // Assert, first, that the walk found the corpus at all: 14 fixtures ship today, so
        // ten is a floor with headroom. Without this the test passes loudest exactly when it
        // has stopped looking at anything - and it used to enumerate a relative "Fixtures",
        // which resolves against the working directory rather than the test binary.
        Assert.True(
            fixtures.Length >= 10,
            $"Only {fixtures.Length} pipeline fixtures were found; this guard has stopped finding the corpus it maps.");

        var inspected = 0;

        foreach (var path in fixtures)
        {
            var model = PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(path)));

            // Act.
            var elements = Map(model, model.Stages.Select(stage => stage.Id).ToArray());

            // Assert.
            Assert.All(elements, AssertElement);
            inspected += elements.Count;
        }

        // And a floor on the second collection: Assert.All over an empty list passes, so
        // finding the corpus is not enough - the mapper has to have produced something out of
        // it before any of those three checks means anything. The corpus maps 64 elements
        // today, so forty is a floor with real headroom on it.
        Assert.True(
            inspected >= 40,
            $"Only {inspected} elements were mapped across the whole corpus; this guard has stopped finding the elements it inspects.");
        return;

        void AssertElement(DiagramElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            Assert.NotEmpty(element.Id);
            Assert.NotEmpty(element.Type);
            Assert.False(element.Payload.IsEmpty, $"{element.Id} carries no payload");
        }
    }
}
