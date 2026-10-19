using System.Text;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The planner turns one <see cref="ModelChange"/> into splices of FBL 6.1 by byte spans (azure-devops-pipeline-disl-fbl
/// task 9, Requirements 5.2, 5.3, 5.4 and 5.6): each test names the splices it expects, by operation, the bytes each
/// replaces and the text it writes, and the document they leave.
/// </summary>
public class PipelinePlannerTests
{
    // Set of displayName and enabled: replace-value in the value's own style.

    [Theory]
    [InlineData("Single", "Renamed", "'Built by quotes'", "'Renamed'")]
    [InlineData("Single", "It's", "'Built by quotes'", "'It''s'")]
    [InlineData("Double", "Say \"hi\"", "\"Built by \\\"quotes\\\"\"", "\"Say \\\"hi\\\"\"")]
    [InlineData("Spaced", "Closer", "Spaced out", "Closer")]
    [InlineData("Spaced", "Build: fast", "Spaced out", "\"Build: fast\"")]
    [InlineData("Typed", "false", "true", "false")]
    public void ANewDisplayName_ReplacesTheValuesOwnSpan_InItsOwnStyle(string stage, string value, string replaced, string written)
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", stage), Values(("displayName", value))));

        // Assert: the value alone, so the spacing after the colon and a trailing comment stay.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, replaced, written)], planned.Splices);
        Assert.Equal(body.Text.Replace(replaced, written, StringComparison.Ordinal), planned.After);
    }

    [Fact]
    public void ANewDisplayName_OfAFoldedValue_IsFoldedAgain()
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Folded"), Values(("displayName", "Folded again"))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, ">-\n      A folded\n      display name", ">-\n      Folded again")], planned.Splices);
        Assert.Contains("  - stage: Folded\n    displayName: >-\n      Folded again\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Build", "${{ parameters.name }}", "${{ format('Build {0}', parameters.deploy) }}")]
    [InlineData("Build", "$[ variables.name ]", "${{ format('Build {0}', parameters.deploy) }}")]
    public void AnExpression_IsWrittenAsWritten_AndNeverQuoted(string stage, string value, string replaced)
    {
        // Arrange.
        var body = Open("edge-expressions.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", stage), Values(("displayName", value))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, replaced, value)], planned.Splices);
    }

    [Fact]
    public void AnExpression_ReplacesAQuotedValue_QuotesAndAll()
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Single"), Values(("displayName", "${{ variables['name: x'] }}"))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "'Built by quotes'", "${{ variables['name: x'] }}")], planned.Splices);
    }

    [Fact]
    public void TheSameValue_IsNoSplice()
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Single"), Values(("displayName", "Built by quotes"))));

        // Assert.
        Assert.Empty(planned.Splices);
        Assert.Equal(body.Text, planned.After);
    }

    // Set of a key the element does not have: insert-key after the whole value of the key before it.

    [Fact]
    public void ANewDisplayName_OfAStepThatOpensWithABlockScalar_GoesAfterTheWholeScript()
    {
        // Arrange: finding B8.
        var body = Open("block-scalar-step.yml");
        var step = StepOf(body, IdOf(body, "Job", "Compile"), 0);

        // Act.
        var planned = Plan(body, new ModelChange.Set(step, Values(("displayName", "Run"))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.InsertKey, "", "            displayName: Run\n")], planned.Splices);
        Assert.Contains("              dotnet build\n\n              dotnet test\n            displayName: Run\n          - bash: >-\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewEnabled_GoesAfterTheKeyBeforeItInTheOrder_HereTheCondition()
    {
        // Arrange: kind key, displayName, dependsOn, condition, enabled.
        var body = Open("block-scalar-step.yml");
        var step = StepOf(body, IdOf(body, "Job", "Compile"), 1);

        // Act.
        var planned = Plan(body, new ModelChange.Set(step, Values(("enabled", "false"))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.InsertKey, "", "            enabled: false\n")], planned.Splices);
        Assert.EndsWith("            condition: always()\n            enabled: false\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewDisplayName_GoesAfterTheKindKey_AndANewEnabledAfterTheDependsOn()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");
        var test = IdOf(body, "Stage", "Test");

        // Act.
        var planned = Plan(body, new ModelChange.Set(test, Values(("enabled", "false"), ("displayName", "Testing"))));

        // Assert: planned in the order of the keys, whatever order the change names them in.
        Assert.Equal(
            [
                new Planned(SpliceOperation.InsertKey, "", "    displayName: Testing\n"),
                new Planned(SpliceOperation.InsertKey, "", "    enabled: false\n"),
            ],
            planned.Splices);
        Assert.Contains("  - stage: Test\n    displayName: Testing\n    dependsOn: Build\n    enabled: false\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyDisplayName_RemovesTheKey(string? value)
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Single"), Values(("displayName", value))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.RemoveKey, "    displayName: 'Built by quotes'\n", "")], planned.Splices);
        Assert.Contains("  - stage: Single\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAKeyTheElementDoesNotHave_IsNoSplice()
    {
        // Arrange.
        var body = Open("findings/value-styles.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Single"), Values(("enabled", null))));

        // Assert.
        Assert.Empty(planned.Splices);
    }

    // Set of dependsOn: one name, a block list, [] or removed (stand-in for language gaps 5 and 6).

    [Fact]
    public void OneName_IsWrittenAsAScalar()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Lint"), Values(("dependsOn", Names("Build")))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "[]", "Build")], planned.Splices);
        Assert.Contains("  - stage: Lint\n    dependsOn: Build\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralNames_AreWrittenAsABlockList()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Test"), Values(("dependsOn", Names("Build", "Lint")))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, " Build", "\n      - Build\n      - Lint")], planned.Splices);
        Assert.Contains("  - stage: Test\n    dependsOn:\n      - Build\n      - Lint\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralNames_ReplacingABlockList_KeepTheListsOwnIndentation()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Pack"), Values(("dependsOn", Names("Test", "Lint")))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "- Build\n      - Test", "- Test\n      - Lint")], planned.Splices);
    }

    [Fact]
    public void NoNames_AreWrittenAsAnEmptyList()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Pack"), Values(("dependsOn", Names()))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "\n      - Build\n      - Test", " []")], planned.Splices);
        Assert.Contains("  - stage: Pack\n    dependsOn: []\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_RemovesTheKey()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Docs"), Values(("dependsOn", null))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.RemoveKey, "    dependsOn: [Build, Test]\n", "")], planned.Splices);
        Assert.Contains("  - stage: Docs\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void NullAndAnEmptyList_AreDifferentPipelines()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");
        var test = IdOf(body, "Stage", "Test");

        // Act.
        var unset = Plan(body, new ModelChange.Set(test, Values(("dependsOn", null))));
        var empty = Plan(body, new ModelChange.Set(test, Values(("dependsOn", Names()))));

        // Assert.
        Assert.NotEqual(unset.After, empty.After);
        Assert.Equal([new Planned(SpliceOperation.RemoveKey, "    dependsOn: Build\n", "")], unset.Splices);
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "Build", "[]")], empty.Splices);
    }

    [Fact]
    public void ANewDependsOn_GoesAfterTheKindKey()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Build"), Values(("dependsOn", Names("Lint", "Docs")))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.InsertKey, "", "    dependsOn:\n      - Lint\n      - Docs\n")], planned.Splices);
        Assert.Contains("  - stage: Build\n    dependsOn:\n      - Lint\n      - Docs\n    jobs:\n", planned.After, StringComparison.Ordinal);
    }

    // Add: insert-entry after the last entry of the list itself, at the file's own indentation.

    [Fact]
    public void ANewStage_GoesAfterTheLastEntryOfTheList_NotIntoTheGatedBlock()
    {
        // Arrange: finding B10.
        var body = Open("findings/gated-last-stage.yml");

        // Act.
        var planned = Plan(body, new ModelChange.Add("Stage", "Ship", Values(("name", "Ship"))));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.InsertEntry, "", "  - stage: Ship\n")], planned.Splices);
        Assert.EndsWith("            - script: dotnet nuget push\n  - stage: Ship\n", planned.After, StringComparison.Ordinal);
        Assert.Equal("", PipelineBody.Parse(planned.After).Model.Stages.Single(stage => stage.Name == "Ship").Gate);
    }

    [Fact]
    public void ANewStage_IsThreeAddsInOneEdit_AtTheFilesOwnIndentation()
    {
        // Arrange: finding B11, lists written flush with their keys.
        var body = Open("findings/flush-lists.yml");
        var planner = new PipelinePlanner(body.Bytes, body.Reading.Elements);

        // Act.
        var refusals = new[]
        {
            planner.Plan(new ModelChange.Add("Stage", "Ship", Values(("name", "Ship")))),
            planner.Plan(new ModelChange.Add("Job", "Ship/ShipJob", Values(("name", "ShipJob")), "Ship")),
            planner.Plan(new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", PipelineBlocks.PlaceholderScript)), "Ship/ShipJob")),
        };
        var planned = Describe(body.Bytes, planner.Splices);

        // Assert.
        Assert.All(refusals, Assert.Null);
        Assert.Equal(
            [
                new Planned(SpliceOperation.InsertEntry, "", "- stage: Ship\n"),
                new Planned(SpliceOperation.EnsureContainer, "", "  jobs:\n"),
                new Planned(SpliceOperation.InsertEntry, "", "  - job: ShipJob\n"),
                new Planned(SpliceOperation.EnsureContainer, "", "    steps:\n"),
                new Planned(SpliceOperation.InsertEntry, "", "    - script: echo Add your build steps here\n"),
            ],
            planned.Splices);
        Assert.EndsWith("    - script: dotnet test\n- stage: Ship\n  jobs:\n  - job: ShipJob\n    steps:\n    - script: echo Add your build steps here\n", planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewStage_InAnIndentedFile_IsIndentedAsTheFileIs()
    {
        // Arrange.
        var body = Open("multi-stage.yml");
        var planner = new PipelinePlanner(body.Bytes, body.Reading.Elements);

        // Act.
        planner.Plan(new ModelChange.Add("Stage", "Ship", Values(("name", "Ship"))));
        planner.Plan(new ModelChange.Add("Job", "Ship/ShipJob", Values(("name", "ShipJob")), "Ship"));
        planner.Plan(new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", PipelineBlocks.PlaceholderScript)), "Ship/ShipJob"));
        var after = Describe(body.Bytes, planner.Splices).After;

        // Assert: what the module's blocks have always written for a new stage.
        var lines = PipelineBlocks.Stage("Ship", 2).Select(line => line + "\n");
        Assert.Contains(string.Concat(lines), after, StringComparison.Ordinal);
        Assert.Single(PipelineBody.Parse(after).Model.Stages, stage => stage.Name == "Ship" && stage.Jobs.Single().Steps.Count == 1);
    }

    [Fact]
    public void ANewJob_GoesAfterTheStagesLastJob_AndANewStepAfterTheJobsLastStep()
    {
        // Arrange.
        var body = Open("edge-indentation.yml");
        var build = IdOf(body, "Stage", "Build");
        var compile = IdOf(body, "Job", "Compile");

        // Act.
        var job = Plan(body, new ModelChange.Add("Job", null, Values(("name", "Pack"), ("displayName", "Pack it")), build));
        var step = Plan(body, new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", "echo more")), compile));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.InsertEntry, "", "  - job: Pack\n    displayName: Pack it\n")], job.Splices);
        Assert.Contains("      displayName: Build\n  - job: Pack\n    displayName: Pack it\n- stage: Test\n", job.After, StringComparison.Ordinal);
        Assert.Equal([new Planned(SpliceOperation.InsertEntry, "", "    - script: echo more\n")], step.Splices);
        Assert.Contains("      displayName: Build\n    - script: echo more\n- stage: Test\n", step.After, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewDeploymentJob_HasItsEnvironmentAndStrategy_AndItsStepGoesInTheHook()
    {
        // Arrange.
        var body = Open("multi-stage.yml");
        var stage = body.Reading.Elements.First(element => element.Type == "Stage").Id;
        var planner = new PipelinePlanner(body.Bytes, body.Reading.Elements);

        // Act.
        planner.Plan(new ModelChange.Add("DeploymentJob", "new-job", Values(("name", "Ship"), ("environment", "ship"), ("strategy", "runOnce")), stage));
        planner.Plan(new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", PipelineBlocks.PlaceholderScript), ("hook", "deploy")), "new-job"));
        var after = Describe(body.Bytes, planner.Splices).After;

        // Assert: what the module's blocks write for a new deployment job, but with the step's list indented under its key, as this file indents its lists.
        Assert.Contains("            artifact: drop\n      - deployment: Ship\n        environment: ship\n        strategy:\n          runOnce:\n            deploy:\n              steps:\n                - script: echo Add your build steps here\n\n  - stage: Test\n", after, StringComparison.Ordinal);
        var read = PipelineBody.Parse(after).Model.Jobs.Single(job => job.Name == "Ship");
        Assert.True(read.IsDeployment);
        Assert.Equal("deploy", Assert.Single(read.Steps).Hook);
    }

    // Remove: remove-entry, taking the entry and its leading comments and no blank line.

    [Fact]
    public void ARemovedStage_TakesItsLeadingComments_AndNoBlankLine()
    {
        // Arrange.
        var body = PipelineBody.Parse("stages:\n  - stage: Build\n    jobs: []\n\n  # The tests.\n  # Both of them.\n  - stage: Test # trailing\n    jobs: []\n\n  - stage: Ship\n    jobs: []\n");

        // Act.
        var planned = Plan(body, new ModelChange.Remove(IdOf(body, "Stage", "Test")));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.RemoveEntry, "  # The tests.\n  # Both of them.\n  - stage: Test # trailing\n    jobs: []\n", "")], planned.Splices);
        Assert.Equal("stages:\n  - stage: Build\n    jobs: []\n\n\n  - stage: Ship\n    jobs: []\n", planned.After);
    }

    [Fact]
    public void ARemovedStep_IsItsOwnLinesOnly()
    {
        // Arrange.
        var body = Open("blank-lines-after.yml");
        var step = StepOf(body, IdOf(body, "Job", "Compile"), 1);

        // Act.
        var planned = Plan(body, new ModelChange.Remove(step));

        // Assert.
        Assert.Equal([new Planned(SpliceOperation.RemoveEntry, "          - script: dotnet pack\n", "")], planned.Splices);
        Assert.Contains("          - script: dotnet build\n\n\n\n          - script: dotnet publish\n", planned.After, StringComparison.Ordinal);
    }

    // Move: remove-entry and insert-entry of the same bytes, in one edit.

    [Fact]
    public void AMovedStep_KeepsItsBytes()
    {
        // Arrange: a step whose script is a block scalar with a blank line in it.
        var body = Open("block-scalar-step.yml");
        var compile = IdOf(body, "Job", "Compile");
        var first = StepOf(body, compile, 0);
        const string bytes = "          - script: |\n              dotnet restore\n              dotnet build\n\n              dotnet test\n";

        // Act: down, after the second.
        var planned = Plan(body, new ModelChange.Move(first, compile, 2));

        // Assert.
        Assert.Equal(
            [
                new Planned(SpliceOperation.RemoveEntry, bytes, ""),
                new Planned(SpliceOperation.InsertEntry, "", bytes),
            ],
            planned.Splices);
        Assert.EndsWith("            condition: always()\n" + bytes, planned.After, StringComparison.Ordinal);
    }

    [Fact]
    public void AStepMovedUp_GoesBeforeTheOneNowAtTheIndex()
    {
        // Arrange.
        var body = Open("blank-lines-after.yml");
        var compile = IdOf(body, "Job", "Compile");
        var last = StepOf(body, compile, 2);

        // Act.
        var planned = Plan(body, new ModelChange.Move(last, compile, 1));

        // Assert.
        Assert.Equal(
            [
                new Planned(SpliceOperation.InsertEntry, "", "          - script: dotnet publish\n"),
                new Planned(SpliceOperation.RemoveEntry, "          - script: dotnet publish\n", ""),
            ],
            planned.Splices);
        Assert.Contains("          - script: dotnet build\n\n          - script: dotnet publish\n          - script: dotnet pack\n\n\n\n      - job: Lint\n", planned.After, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public void AStepMovedToWhereItIs_IsNoSplice(int step, int index)
    {
        // Arrange: before itself, before the one after it across a blank line, and after the last when it is the last.
        var body = Open("blank-lines-after.yml");
        var compile = IdOf(body, "Job", "Compile");

        // Act.
        var planned = Plan(body, new ModelChange.Move(StepOf(body, compile, step), compile, index));

        // Assert.
        Assert.Empty(planned.Splices);
    }

    // Refused, with no splice: what is implicit, from a template, or arrives through a merge.

    [Fact]
    public void AnImplicitElement_IsRefusedWithTheReadOnlyReason()
    {
        // Arrange.
        var body = Open("steps-only.yml");
        var job = body.Reading.Elements.Single(element => element.Type == "ImplicitJob").Id;
        const string reason = "This is implied by the pipeline's shape rather than written down, so there is nothing here to edit.";

        // Act and assert.
        Assert.Equal(reason, Refusal(body, new ModelChange.Set(job, Values(("displayName", "Build")))));
        Assert.Equal(reason, Refusal(body, new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", "echo")), job)));
        Assert.Equal(reason, Refusal(body, new ModelChange.Remove(job)));
    }

    [Fact]
    public void AnElementFromATemplate_IsRefusedWithTheReadOnlyReason()
    {
        // Arrange.
        var body = OpenInFolder("templates.yml");
        var step = body.Reading.Elements.First(element => element.Type == "Step" && element.Attributes.ContainsKey("template"));
        var reason = $"This comes from {step.Attributes["template"]}, so it has to be edited there.";

        // Act and assert.
        Assert.Equal(reason, Refusal(body, new ModelChange.Set(step.Id, Values(("displayName", "Renamed")))));
        Assert.Equal(reason, Refusal(body, new ModelChange.Remove(step.Id)));
        Assert.Equal(reason, Refusal(body, new ModelChange.Move(step.Id, step.ParentId, 0)));
    }

    [Fact]
    public void AKeyThatArrivesThroughAMerge_IsRefused_AndTheElementsOwnKeyIsWritten()
    {
        // Arrange: finding B13.
        var body = Open("findings/merge-key-written.yml");
        const string reason = "The value is reached through a merge key, so it is read-only.";

        // Act.
        var own = Plan(body, new ModelChange.Set(IdOf(body, "Stage", "Test"), Values(("displayName", "Tested"))));

        // Assert.
        Assert.Equal(reason, Refusal(body, new ModelChange.Set(IdOf(body, "Stage", "Build"), Values(("displayName", "Renamed")))));
        Assert.Equal(reason, Refusal(body, new ModelChange.Set(IdOf(body, "Stage", "Build"), Values(("enabled", null)))));
        Assert.Equal(reason, Refusal(body, new ModelChange.Set(IdOf(body, "Stage", "Test"), Values(("enabled", "false")))));
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "Own name", "Tested")], own.Splices);
    }

    [Fact]
    public void AnythingButTheThreeKeys_IsRefused()
    {
        // Arrange.
        var body = Open("findings/depends-on-forms.yml");
        var test = IdOf(body, "Stage", "Test");

        // Act and assert.
        Assert.Equal("Only a stage's, a job's or a step's displayName, dependsOn and enabled are written here.", Refusal(body, new ModelChange.Set(test, Values(("condition", "always()")))));
        Assert.Equal("That element is no longer in this pipeline.", Refusal(body, new ModelChange.Set("read-999", Values(("displayName", "x")))));
    }

    [Fact]
    public void AddingAStage_ToAPipelineWithoutAStagesBlock_IsRefused()
    {
        // Arrange.
        var body = Open("jobs-only.yml");

        // Act and assert.
        Assert.Equal(
            "This pipeline has no stages block to add a stage to. Add one in the file first.",
            Refusal(body, new ModelChange.Add("Stage", "Ship", Values(("name", "Ship")))));
    }

    [Fact]
    public void ARefusedChange_LeavesWhatWasPlannedBeforeIt()
    {
        // Arrange.
        var body = Open("findings/merge-key-written.yml");
        var planner = new PipelinePlanner(body.Bytes, body.Reading.Elements);
        var test = IdOf(body, "Stage", "Test");

        // Act: the first is planned, the second refused as a whole, its rename included.
        var first = planner.Plan(new ModelChange.Set(test, Values(("displayName", "Tested"))));
        var second = planner.Plan(new ModelChange.Set(IdOf(body, "Stage", "Build"), Values(("dependsOn", Names()), ("displayName", "Renamed"))));

        // Assert.
        Assert.Null(first);
        Assert.NotNull(second);
        Assert.Equal([new Planned(SpliceOperation.ReplaceValue, "Own name", "Tested")], Describe(body.Bytes, planner.Splices).Splices);
    }

    [Fact]
    public void CrlfIsKept_InEverySpliceWritten()
    {
        // Arrange.
        var body = PipelineBody.Parse("stages:\r\n  - stage: Build\r\n    jobs:\r\n      - job: A\r\n        steps:\r\n          - script: x\r\n");
        var planner = new PipelinePlanner(body.Bytes, body.Reading.Elements);

        // Act.
        planner.Plan(new ModelChange.Set(IdOf(body, "Stage", "Build"), Values(("enabled", "false"), ("dependsOn", Names("A", "B")))));
        planner.Plan(new ModelChange.Add("Step", null, Values(("stepKind", "script"), ("identifier", "y")), IdOf(body, "Job", "A")));
        var planned = Describe(body.Bytes, planner.Splices);

        // Assert.
        Assert.Equal(
            [
                new Planned(SpliceOperation.InsertKey, "", "    dependsOn:\r\n      - A\r\n      - B\r\n"),
                new Planned(SpliceOperation.InsertKey, "", "    enabled: false\r\n"),
                new Planned(SpliceOperation.InsertEntry, "", "          - script: y\r\n"),
            ],
            planned.Splices);
    }

    /// <summary>A splice as a test names it: its operation, the bytes it replaces and the text it writes.</summary>
    private sealed record Planned(SpliceOperation Operation, string Replaced, string Text)
    {
        public override string ToString() => $"{Operation} [{Replaced}] {Text}";
    }

    /// <summary>What a plan gave: its splices and the document they leave.</summary>
    private sealed record Result(IReadOnlyList<Planned> Splices, string After);

    private static Result Plan(PipelineBody body, ModelChange change)
    {
        var result = PipelinePlanner.PlanOne(body.Bytes, body.Reading.Elements, change);
        var planned = Assert.IsType<PluginPlanResult.Planned>(result, exactMatch: true);
        return Describe(body.Bytes, [.. planned.Splices.Select(splice => splice.Splice)]);
    }

    private static string Refusal(PipelineBody body, ModelChange change) =>
        Assert.IsType<PluginPlanResult.Refused>(PipelinePlanner.PlanOne(body.Bytes, body.Reading.Elements, change), exactMatch: true).Reason;

    private static Result Describe(byte[] before, IReadOnlyList<Splice> splices) =>
        new(
            [.. splices.Select(splice => new Planned(splice.Operation, Encoding.UTF8.GetString(before, splice.Start, splice.End - splice.Start), splice.Text))],
            Encoding.UTF8.GetString(Edit.Apply(before, splices)));

    private static string IdOf(PipelineBody body, string type, string name) =>
        body.Reading.Elements.Single(element => element.Type == type && Equals(element.Attributes.GetValueOrDefault("name"), name)).Id;

    private static string StepOf(PipelineBody body, string jobId, int index) =>
        body.Reading.Elements.Where(element => element.Type == "Step" && element.ParentId == jobId).ElementAt(index).Id;

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private static List<object?> Names(params string[] names) => [.. names];

    private static string FixturePath(string name) => IoPath.Combine(PipelineCorpus.ModuleFolder, "backend", PipelineCorpus.TestProject, "Fixtures", name);

    private static PipelineBody Open(string name) => PipelineBody.Open(File.ReadAllBytes(FixturePath(name)), PipelineReadContext.Unbound);

    private static PipelineBody OpenInFolder(string name)
    {
        var path = FixturePath(name);
        return PipelineBody.Open(File.ReadAllBytes(path), new PipelineReadContext(path, new PipelineTemplates(ProjectRoot.From(IoPath.GetDirectoryName(path)!))));
    }
}
