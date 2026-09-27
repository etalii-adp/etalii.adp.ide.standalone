using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The parser's two promises: the corpus reads into a model in Azure's own vocabulary, and every
/// element knows the lines that declare it - the second being what the writer later splices
/// through, so a range that points at the wrong lines is a file corrupted rather than a label
/// misplaced.
/// </summary>
public class PipelineParserTests
{
    private static PipelineModel ParseFixture(string name) =>
        PipelineParser.Parse(LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name))));

    private static LineDocument FixtureDocument(string name) =>
        LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name)));

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles("Fixtures", "*.yml", SearchOption.AllDirectories))
        {
            data.Add(IoPath.GetRelativePath("Fixtures", path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixture_Parses(string relativePath)
    {
        // Arrange.
        var document = LineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", relativePath)));

        // Act.
        var model = PipelineParser.Parse(document);

        // Assert.
        // Anchors, merge keys, unindented sequences and compile-time expressions all appear in
        // this corpus deliberately: the point is that none of them stops the file being read.
        Assert.NotNull(model);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryElement_PointsAtItsOwnDeclaration(string relativePath)
    {
        // Arrange.
        var document = FixtureDocument(relativePath);
        var model = PipelineParser.Parse(document);

        // Act.
        var elements = model.Stages
            .Select(stage => (Kind: "stage", stage.Id, stage.Lines))
            .Concat(model.Jobs.Select(job => (Kind: "job", job.Id, job.Lines)))
            .Concat(model.Steps.Select(step => (Kind: "step", step.Id, step.Lines)))
            .ToList();

        // Assert.
        // A range must be inside the document, must not be inverted, and must not start on a line
        // that carries nothing - a blank or a comment as a first line means the range drifted off
        // the declaration onto the whitespace above the next one.
        foreach (var (kind, id, lines) in elements)
        {
            Assert.InRange(lines.Start, 0, document.Lines.Count - 1);
            Assert.InRange(lines.End, lines.Start, document.Lines.Count - 1);
            var first = document.Lines[lines.Start];
            Assert.False(first.IsBlank, $"{kind} {id} starts on a blank line");
            Assert.False(first.IsComment, $"{kind} {id} starts on a comment line");
        }
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void AnElementsRange_ContainsItsChildrens(string relativePath)
    {
        // Arrange.
        var document = FixtureDocument(relativePath);

        // Act.
        var model = PipelineParser.Parse(document);

        // Assert.
        // Containment is the property that makes a range usable: a stage that reports only the one
        // line carrying its name would satisfy every check above and still be wrong, and a writer
        // splicing inside it would land outside the stage.
        foreach (var stage in model.Stages)
        {
            foreach (var job in stage.Jobs)
            {
                Assert.True(
                    job.Lines.Start >= stage.Lines.Start && job.Lines.End <= stage.Lines.End,
                    $"job {job.Id} at {job.Lines} escapes stage {stage.Id} at {stage.Lines}");

                foreach (var step in job.Steps)
                {
                    Assert.True(
                        step.Lines.Start >= job.Lines.Start && step.Lines.End <= job.Lines.End,
                        $"step {step.Id} at {step.Lines} escapes job {job.Id} at {job.Lines}");
                }
            }
        }
    }

    [Fact]
    public void AMultiStagePipeline_ReadsItsStagesInOrder()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        Assert.Equal(
            ["Build", "Test", "DeployStaging", "DeployProduction", "Notify"],
            model.Stages.Select(stage => stage.Name));
        Assert.All(model.Stages, stage => Assert.False(stage.IsImplicit));
    }

    [Fact]
    public void AStagesDeclaration_PointsAtTheLineHoldingItsName()
    {
        // Arrange.
        var document = FixtureDocument("multi-stage.yml");

        // Act.
        var model = PipelineParser.Parse(document);
        var test = model.Stages.Single(stage => stage.Name == "Test");

        // Assert.
        // The range's first line is the one a rename would rewrite, so it must be the `- stage:`
        // line itself and not the blank or comment separating it from the stage before.
        Assert.Contains("stage: Test", document.Lines[test.Lines.Start].Text);
        Assert.Contains("Integration tests", document.Lines[test.Lines.End].Text);
    }

    [Fact]
    public void AStagesRange_DoesNotSwallowTheCommentIntroducingTheNextStage()
    {
        // Arrange: multi-stage.yml puts a comment above the Notify stage's dependsOn and a blank
        // line between every stage. A range that ran to the next stage's start would take them.
        var document = FixtureDocument("multi-stage.yml");

        // Act.
        var model = PipelineParser.Parse(document);
        var build = model.Stages.Single(stage => stage.Name == "Build");

        // Assert.
        Assert.False(document.Lines[build.Lines.End].IsBlank);
        Assert.False(document.Lines[build.Lines.End].IsComment);
        Assert.DoesNotContain("stage: Test", document.Lines[build.Lines.End].Text);
    }

    [Fact]
    public void AJobsOnlyFile_YieldsOneImplicitStage()
    {
        // Arrange & act.
        var model = ParseFixture("jobs-only.yml");

        // Assert.
        var stage = Assert.Single(model.Stages);
        Assert.True(stage.IsImplicit);
        Assert.Equal("", stage.Name);
        Assert.Equal(["Build", "Test"], stage.Jobs.Select(job => job.Name));
        Assert.All(stage.Jobs, job => Assert.False(job.IsImplicit));
    }

    [Fact]
    public void AStepsOnlyFile_YieldsOneImplicitStageAroundOneImplicitJob()
    {
        // Arrange & act.
        var model = ParseFixture("steps-only.yml");

        // Assert.
        var stage = Assert.Single(model.Stages);
        Assert.True(stage.IsImplicit);
        var job = Assert.Single(stage.Jobs);
        Assert.True(job.IsImplicit);
        Assert.Equal(["Build", "Test"], job.Steps.Select(step => step.Label));
        Assert.Equal(2, job.Steps.Count);
    }

    [Fact]
    public void ADeploymentJob_CarriesItsEnvironmentAndStrategy()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var staging = model.Jobs.Single(job => job.Name == "Staging");
        Assert.True(staging.IsDeployment);
        Assert.Equal("staging", staging.Environment);
        Assert.Equal(PipelineStrategyKind.RunOnce, staging.Strategy.Kind);
        Assert.True(staging.Strategy.IsDeployment);
    }

    [Fact]
    public void ADeploymentJobsSteps_ComeFromItsLifecycleHooks()
    {
        // Arrange: a deployment job declares no `steps` of its own - they live under
        // strategy.runOnce.deploy.steps, and a reader who cannot see them sees an empty job.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var staging = model.Jobs.Single(job => job.Name == "Staging");

        // Assert.
        Assert.Equal(
            [PipelineStepKind.Download, PipelineStepKind.Script],
            staging.Steps.Select(step => step.Kind));
        Assert.All(staging.Steps, step => Assert.Equal("deploy", step.Hook));
    }

    [Fact]
    public void APlainJobsSteps_CarryNoHook()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var compile = model.Jobs.Single(job => job.Name == "Compile");
        Assert.All(compile.Steps, step => Assert.Equal("", step.Hook));
    }

    [Fact]
    public void EachStepKind_IsReadFromTheKeyItLeadsWith()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        var compile = model.Jobs.Single(job => job.Name == "Compile");
        Assert.Equal(
            [PipelineStepKind.Checkout, PipelineStepKind.Script, PipelineStepKind.Publish],
            compile.Steps.Select(step => step.Kind));
        Assert.Equal("self", compile.Steps[0].Identifier);
    }

    [Fact]
    public void AStepWithoutADisplayName_FallsBackToItsKindsIdentifyingValue()
    {
        // Arrange: the checkout step in multi-stage.yml has no displayName, so Requirement 4.4
        // says its own value stands in rather than the label being blank.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var checkout = model.Jobs.Single(job => job.Name == "Compile").Steps[0];

        // Assert.
        Assert.Equal("", checkout.DisplayName);
        Assert.Equal("self", checkout.Label);
    }

    [Fact]
    public void AMultiLineScript_DoesNotBecomeAMultiLineLabel()
    {
        // Arrange.
        var document = LineDocument.Parse(
            "steps:\n  - script: |\n      echo one\n      echo two\n");

        // Act.
        var step = PipelineParser.Parse(document).Steps.Single();

        // Assert.
        Assert.Equal("echo one", step.Label);
    }

    [Fact]
    public void DependsOnAsAScalarAndAsAList_BothRead()
    {
        // Arrange & act.
        var model = ParseFixture("multi-stage.yml");

        // Assert.
        Assert.Equal(["Build"], model.Stages.Single(stage => stage.Name == "Test").DependsOn);
        Assert.Equal(
            ["DeployStaging", "DeployProduction"],
            model.Stages.Single(stage => stage.Name == "Notify").DependsOn);
    }

    [Fact]
    public void AnAbsentDependsOn_IsNotTheSameAsAnEmptyOne()
    {
        // Arrange: for a stage the difference decides whether it inherits the sequential default,
        // so the two must be distinguishable in the model rather than both reading as "no names".
        var model = ParseFixture("edge-anchors.yml");

        // Act.
        var one = model.Stages.Single(stage => stage.Name == "One");
        var two = model.Stages.Single(stage => stage.Name == "Two");

        // Assert.
        Assert.False(one.DependsOnDeclared);
        Assert.Empty(one.DependsOn);
        Assert.True(two.DependsOnDeclared);
        Assert.Empty(two.DependsOn);
    }

    [Fact]
    public void AMergeKey_DoesNotBecomeAStageOfItsOwn()
    {
        // Arrange: `<<: *defaults` is a plain key to a YAML reader. Left alone it would show up
        // as a property named "<<" and hide the pool it merges in.
        var model = ParseFixture("edge-anchors.yml");

        // Assert.
        Assert.Equal(["One", "Two"], model.Stages.Select(stage => stage.Name));
    }

    [Fact]
    public void AnUnindentedSequence_ReadsLikeAnIndentedOne()
    {
        // Arrange: valid YAML that a reformatter would silently "fix", so the parser must not
        // depend on the indentation style the author chose.
        var model = ParseFixture("edge-indentation.yml");

        // Assert.
        Assert.Equal(["Build", "Test"], model.Stages.Select(stage => stage.Name));
        Assert.Equal("Build", model.Jobs.Single(job => job.Name == "Compile").Steps[0].Label);
    }

    [Fact]
    public void AStageInsideACompileTimeConditional_StillAppears()
    {
        // Arrange: whether it exists is decided at compile time, which this module refuses to
        // decide - so it appears, and carries the expression that gates it (Requirement 5.1's
        // principle: never silently drop part of the pipeline).
        var model = ParseFixture("edge-expressions.yml");

        // Act.
        var deploy = model.Stages.SingleOrDefault(stage => stage.Name == "Deploy");

        // Assert.
        Assert.NotNull(deploy);
        Assert.Contains("if parameters.deploy", deploy.Gate);
        Assert.Equal(["Build", "Deploy", "Report"], model.Stages.Select(stage => stage.Name));
    }

    [Fact]
    public void AnExpressionValuedProperty_SurvivesVerbatim()
    {
        // Arrange & act.
        var model = ParseFixture("edge-expressions.yml");

        // Assert.
        var build = model.Stages.Single(stage => stage.Name == "Build");
        Assert.Equal("${{ format('Build {0}', parameters.deploy) }}", build.DisplayName);
    }

    [Fact]
    public void AStageLevelTemplate_IsRecordedRatherThanDropped()
    {
        // Arrange & act.
        var model = ParseFixture("templates.yml");

        // Assert.
        var stageTemplate = Assert.Single(model.Templates, template => template.Slot == PipelineTemplateSlot.Stages);
        Assert.Equal("templates/deploy-stages.yml", stageTemplate.Path);
        Assert.Equal("shared", stageTemplate.Resource);
        Assert.Equal("templates/deploy-stages.yml@shared", stageTemplate.Reference);
        Assert.Equal(["environment"], stageTemplate.ParameterNames);
    }

    [Fact]
    public void AJobLevelTemplate_RemembersTheStageItSitsIn()
    {
        // Arrange & act.
        var model = ParseFixture("templates.yml");

        // Assert.
        var jobTemplate = Assert.Single(model.Templates, template => template.Slot == PipelineTemplateSlot.Jobs);
        Assert.Equal("templates/build-jobs.yml", jobTemplate.Path);
        Assert.Equal("", jobTemplate.Resource);
        Assert.Equal("Build", jobTemplate.OwnerId);
    }

    [Fact]
    public void AStepLevelTemplate_IsAStepOfKindTemplate()
    {
        // Arrange: a steps-list template appears where steps appear, so it is a step - modelling
        // it anywhere else would put it in the wrong place on the canvas.
        var model = ParseFixture("templates.yml");

        // Act.
        var step = model.Steps.Single(candidate => candidate.Kind == PipelineStepKind.Template);

        // Assert.
        Assert.Equal("templates/test-steps.yml", step.Identifier);
        Assert.Equal("templates/test-steps.yml", step.Label);
    }

    [Fact]
    public void AnExtendingFile_DeclaresNoStagesOfItsOwn()
    {
        // Arrange & act.
        var model = ParseFixture("extends.yml");

        // Assert.
        // Requirement 5.5: the pipeline's real shape is the template's, and what this file
        // contributes is parameters - the one case where a diagram of a file is not about it.
        Assert.Empty(model.Stages);
        Assert.NotNull(model.Extends);
        Assert.Equal("templates/pipeline.yml", model.Extends.Path);
        Assert.Equal(["buildConfiguration", "deployTo"], model.Extends.ParameterNames);
    }

    [Fact]
    public void ElementIds_AreUniqueWithinADocument()
    {
        // Arrange: the id is what a delta addresses, so a collision would move the wrong element.
        var model = ParseFixture("multi-stage.yml");

        // Act.
        var ids = model.Stages.Select(stage => stage.Id)
            .Concat(model.Jobs.Select(job => job.Id))
            .Concat(model.Steps.Select(step => step.Id))
            .ToList();

        // Assert.
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ADocumentWithNoStagesJobsOrSteps_ParsesToNothingRatherThanThrowing()
    {
        // Arrange: a file that is valid YAML but declares nothing this module draws.
        var document = LineDocument.Parse("variables:\n  buildConfiguration: Release\n");

        // Act.
        var model = PipelineParser.Parse(document);

        // Assert.
        Assert.Empty(model.Stages);
        Assert.Null(model.Extends);
    }
}
