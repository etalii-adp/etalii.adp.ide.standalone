using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// What a selected element shows in the Property Grid.
/// </summary>
/// <remarks>
/// The judgement this module makes here is that almost everything is shown and almost nothing is
/// editable: a pipeline is executable configuration, a text editor edits a pool or a condition
/// better, and a bad write breaks a build. So most of these tests are about the two halves of that
/// - the panel answers "what decides whether this runs", and it says why it will not let you
/// change most of the answer.
/// </remarks>
public class PipelineContextPropertyProviderTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-properties-" + Guid.NewGuid().ToString("N"));

    private readonly PipelineDocumentStore _store = new();
    private readonly HistoryStackStore _historyStacks;
    private readonly PipelineContextPropertyProvider _provider;

    public PipelineContextPropertyProviderTests()
    {
        Directory.CreateDirectory(_workspace);
        _historyStacks = new HistoryStackStore(new PipelineTestDispatcher(_store));
        _provider = new PipelineContextPropertyProvider(_historyStacks, _store);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Pipeline = """
        pool:
          vmImage: ubuntu-latest

        stages:
          - stage: Build
            displayName: Build the solution
            jobs:
              - job: Compile
                steps:
                  - script: |
                      dotnet build
                      dotnet pack
                    displayName: Build it
          - stage: Test
            dependsOn: Build
            condition: succeeded()
            trigger: manual
            jobs:
              - job: Verify
                timeoutInMinutes: 30
                continueOnError: true
                strategy:
                  matrix:
                    linux:
                      image: ubuntu-latest
                    windows:
                      image: windows-latest
                steps:
                  - script: dotnet test
                    enabled: false
        """;

    private string Write(string content = Pipeline)
    {
        var path = IoPath.Combine(_workspace, "azure-pipelines.yml");
        File.WriteAllText(path, content);
        _store.Forget(path);
        return path;
    }

    private ContextTarget Target(string path, string elementId) =>
        new(ContextScope.DiagramElement, path, IsContainer: false, SourceId: default, _workspace, ShortGuid.NewShortGuid(), elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(string path, string elementId) =>
        await _provider.DescribeAsync(Target(path, elementId), CancellationToken.None);

    private async Task<ContextPropertyDefinition?> PropertyAsync(string path, string elementId, string propertyId) =>
        (await DescribeAsync(path, elementId)).FirstOrDefault(property => property.Id == propertyId);

    private async Task<ContextPropertyResult> SetAsync(string path, string elementId, string propertyId, string value) =>
        await _provider.SetAsync(Target(path, elementId), propertyId, value, CancellationToken.None);

    [Fact]
    public void ItContributesToTheDiagramElementScope()
    {
        // Assert.
        Assert.Equal(ContextScope.DiagramElement, _provider.Scope);
    }

    [Fact]
    public async Task AStage_ShowsWhatDecidesWhetherAndHowItRuns()
    {
        // Arrange & act.
        var path = Write();
        var properties = await DescribeAsync(path, "Test");

        // Assert.
        // Requirement 13.2 - the reader should not have to open the YAML to find these.
        var labels = properties.Select(property => property.Label).ToList();
        Assert.Contains("Name", labels);
        Assert.Contains("Depends on", labels);
        Assert.Contains("Condition", labels);
        Assert.Contains("Trigger", labels);
        Assert.Contains("Pool", labels);
        Assert.Contains("Jobs", labels);
    }

    [Fact]
    public async Task AStagesImplicitOrdering_IsShownAsWhatItResolvesTo()
    {
        // Arrange: Requirement 13.2 - a stage relying on the sequential default does wait for
        // something, and an empty row would say it did not.
        var path = Write();

        // Act.
        var dependsOn = await PropertyAsync(path, "Test", PipelineContextPropertyProvider.DependsOnPropertyId);
        var implicitOne = await PropertyAsync(path, "Build", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Equal("Build", dependsOn!.Value);
        Assert.Equal("Depends on", dependsOn.Label);
        // Build is first, so it genuinely waits for nothing - and the label says the value came
        // from the default rather than from the file.
        Assert.Contains("by default", implicitOne!.Label);
    }

    [Fact]
    public async Task AJobWithNoDependsOn_SaysWhatThatMeansRatherThanShowingNothing()
    {
        // Arrange: jobs default to running as soon as their stage does, which is the opposite of
        // the stage default and the thing people get wrong.
        var path = Write();

        // Act.
        var dependsOn = await PropertyAsync(path, "Build/Compile", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Contains("nothing", dependsOn!.Value);
        Assert.Contains("by default", dependsOn.Label);
    }

    [Fact]
    public async Task AJob_ShowsItsStrategyAndWhatItMultipliesTo()
    {
        // Arrange & act.
        var path = Write();
        var properties = await DescribeAsync(path, "Test/Verify");

        // Assert.
        Assert.Equal("Matrix", properties.Single(property => property.Label == "Strategy").Value);
        Assert.Equal("2 times", properties.Single(property => property.Label == "Runs").Value);
        Assert.Equal("30", properties.Single(property => property.Label == "Timeout (minutes)").Value);
        Assert.Equal("true", properties.Single(property => property.Label == "Continue on error").Value);
    }

    [Fact]
    public async Task ADeploymentJob_SaysSoAndShowsItsEnvironment()
    {
        // Arrange & act.
        var path = Write("""
            stages:
              - stage: Deploy
                jobs:
                  - deployment: Ship
                    environment: production
                    strategy:
                      runOnce:
                        deploy:
                          steps:
                            - script: ./deploy.sh
            """);
        var properties = await DescribeAsync(path, "Deploy/Ship");

        // Assert.
        Assert.Equal("Deployment job", properties.Single(property => property.Label == "Kind").Value);
        Assert.Equal("production", properties.Single(property => property.Label == "Environment").Value);
        Assert.Equal("RunOnce", properties.Single(property => property.Label == "Strategy").Value);
    }

    [Fact]
    public async Task AStep_ShowsItsKindAndTheValueThatIdentifiesIt()
    {
        // Arrange & act.
        var path = Write();
        var properties = await DescribeAsync(path, "Build/Compile/step-0");

        // Assert.
        Assert.Equal("Script", properties.Single(property => property.Label == "Kind").Value);
        var identifier = properties.Single(property => property.Id == "azure-pipeline.identifier");
        Assert.Contains("dotnet build", identifier.Value);
        // A script is routinely several lines, and a one-line box would hide most of it.
        Assert.Equal(ContextPropertyEditor.Text, identifier.Editor);
    }

    [Fact]
    public async Task ExactlyTheThreeEditableProperties_AreEditable()
    {
        // Arrange: Requirement 13.6 - displayName, dependsOn and enabled, and nothing else. A
        // pool, a strategy, an environment and a condition are shown but written in the file.
        var path = Write();

        // Act.
        var editable = new List<string>();
        foreach (var elementId in new[] { "Test", "Test/Verify", "Test/Verify/step-0" })
        {
            editable.AddRange((await DescribeAsync(path, elementId))
                .Where(property => property.IsEditable)
                .Select(property => property.Id));
        }

        // Assert.
        Assert.Equal(
            [
                PipelineContextPropertyProvider.DependsOnPropertyId,
                PipelineContextPropertyProvider.DisplayNamePropertyId,
                PipelineContextPropertyProvider.EnabledPropertyId,
            ],
            editable.Distinct().Order());
    }

    [Fact]
    public async Task AReadOnlyProperty_SaysWhyRatherThanJustBeingGrey()
    {
        // Arrange: a reason answers the reader's next question - what would have to change
        // instead - which a greyed-out box does not.
        var path = Write();

        // Act.
        var condition = await PropertyAsync(path, "Test", "azure-pipeline.condition");

        // Assert.
        Assert.False(condition!.IsEditable);
        Assert.Contains("edited in the pipeline file", condition.ReadOnlyReason);
    }

    [Fact]
    public async Task AnInheritedPool_SaysWhereItWasSet()
    {
        // Arrange: "ubuntu-latest" answers where it runs; "set for the whole pipeline" answers
        // where to go and change it. The second belongs in the reason, not in the value.
        var path = Write();

        // Act.
        var pool = await PropertyAsync(path, "Build/Compile", "azure-pipeline.pool");

        // Assert.
        Assert.Equal("ubuntu-latest", pool!.Value);
        Assert.Contains("whole pipeline", pool.ReadOnlyReason);
    }

    [Fact]
    public async Task AnAbsentProperty_IsNotContributedAtAll()
    {
        // Arrange: Requirement 13.10 - a stage with no condition and a stage with an empty one
        // are different states of the file, and a sentinel value would flatten them into one.
        var path = Write();

        // Act.
        var absent = await PropertyAsync(path, "Build", "azure-pipeline.condition");
        var present = await PropertyAsync(path, "Test", "azure-pipeline.condition");

        // Assert.
        Assert.Null(absent);
        Assert.NotNull(present);
    }

    [Fact]
    public async Task AnEmptyValueIsAContributedRow_NotAMissingOne()
    {
        // Arrange: the other half of the same rule, and the half that needs the model to remember
        // which keys the file carried - both states leave the string empty.
        var path = Write("stages:\n  - stage: Build\n    condition: ''\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Act.
        var condition = await PropertyAsync(path, "Build", "azure-pipeline.condition");

        // Assert.
        // The file says the condition is empty, which is a thing it says - so there is a row and
        // its value is empty.
        Assert.NotNull(condition);
        Assert.Equal("", condition.Value);
    }

    [Fact]
    public async Task AnExpressionValuedProperty_ShowsTheExpressionRatherThanAGuess()
    {
        // Arrange: Requirement 13.9 - it is not knowable until the pipeline runs, and showing a
        // resolved-looking value would be a lie the user acts on.
        var path = Write("""
            stages:
              - stage: Build
                condition: eq(variables['Build.Reason'], 'Manual')
                jobs:
                  - job: A
                    steps:
                      - script: x
            """);

        // Act.
        var condition = await PropertyAsync(path, "Build", "azure-pipeline.condition");

        // Assert.
        Assert.Equal("eq(variables['Build.Reason'], 'Manual')", condition!.Value);
    }

    [Fact]
    public async Task AnEnabledDecidedByAnExpression_IsNotOfferedAsAToggle()
    {
        // Arrange: there is no boolean here to flip - it is decided when the pipeline runs.
        var path = Write("steps:\n  - script: x\n    enabled: $(runIt)\n");

        // Act.
        var enabled = await PropertyAsync(path, "stage-0/job-0/step-0", PipelineContextPropertyProvider.EnabledPropertyId);

        // Assert.
        Assert.False(enabled!.IsEditable);
        Assert.Contains("when the pipeline runs", enabled.ReadOnlyReason);
    }

    [Fact]
    public async Task EveryPropertyOfATemplateSourcedElement_NamesTheFileItLivesIn()
    {
        // Arrange: Requirement 13.7 - the case that makes a reason worth more than a flag. The
        // value is real and worth showing, and unwritable here; the reader's next question is
        // where it is written.
        Directory.CreateDirectory(IoPath.Combine(_workspace, "templates"));
        await File.WriteAllTextAsync(IoPath.Combine(_workspace, "templates", "jobs.yml"), "jobs:\n  - job: FromTemplate\n    displayName: From a template\n    steps:\n      - script: x\n", TestContext.Current.CancellationToken);
        var path = Write("stages:\n  - stage: Build\n    jobs:\n      - template: templates/jobs.yml\n");

        // Act.
        var properties = await DescribeAsync(path, "Build/FromTemplate");

        // Assert.
        Assert.NotEmpty(properties);
        Assert.All(properties, property =>
        {
            ArgumentNullException.ThrowIfNull(property);

            Assert.False(property.IsEditable);
            Assert.Contains("templates/jobs.yml", property.ReadOnlyReason);
        });
    }

    [Fact]
    public async Task PropertiesAreGrouped_SoAStageDoesNotReadAsOneFlatList()
    {
        // Arrange: Requirement 13.8 - identity, ordering, execution, because those are the three
        // questions a reader is asking.
        var path = Write();

        // Act.
        var groups = (await DescribeAsync(path, "Test")).Select(property => property.Group).Distinct().ToList();

        // Assert.
        Assert.Contains(PipelineContextPropertyProvider.IdentityGroup, groups);
        Assert.Contains(PipelineContextPropertyProvider.OrderingGroup, groups);
        Assert.Contains(PipelineContextPropertyProvider.ExecutionGroup, groups);
    }

    [Fact]
    public async Task EveryPropertyHasItsOwnIdWithinAnElement()
    {
        // Arrange: SetAsync is given the id back, so two rows sharing one would write the wrong
        // property.
        var path = Write();

        // Act & assert.
        foreach (var elementId in new[] { "Test", "Test/Verify", "Test/Verify/step-0" })
        {
            var ids = (await DescribeAsync(path, elementId)).Select(property => property.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public async Task SettingTheDisplayName_GoesThroughTheSameCommandTheCanvasUses()
    {
        // Arrange: Requirement 13.11 - a rename from the panel and a rename from the diagram are
        // one implementation, and one undo.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await SetAsync(path, "Build", PipelineContextPropertyProvider.DisplayNamePropertyId, "Built");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("displayName: Built", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        // Act.
        await _historyStacks.Get(_workspace).UndoAsync(CancellationToken.None);

        // Assert.
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettingEnabled_SwitchesTheElement()
    {
        // Arrange & act.
        var path = Write();
        var result = await SetAsync(path, "Test/Verify/step-0", PipelineContextPropertyProvider.EnabledPropertyId, "true");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_store.GetOrLoad(_workspace, path).Model.Steps.Last().Execution.IsDisabled);
    }

    /// <summary>Three stages, so there is something for the last one to choose between.</summary>
    private const string ThreeStages = """
        stages:
          - stage: A
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: B
            dependsOn: []
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: C
            jobs:
              - job: J
                steps:
                  - script: x
        """;

    [Fact]
    public async Task DependsOn_IsOfferedAsAListOfTheNamesItCouldWaitFor()
    {
        // Arrange: Requirement 13.14 - the point of a Choice here is that a free-text box gives
        // the user no idea what the valid names are.
        var path = Write(ThreeStages);

        // Act.
        var dependsOn = await PropertyAsync(path, "C", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Equal(ContextPropertyEditor.Choice, dependsOn!.Editor);
        Assert.Contains("A", dependsOn.Choices);
        Assert.Contains("B", dependsOn.Choices);
    }

    [Fact]
    public async Task TheCandidates_LeaveOutStagesThatWouldCloseALoop()
    {
        // Arrange: a list should not offer what the command is going to refuse. A stage can only
        // wait for one declared before it.
        var path = Write(ThreeStages);

        // Act.
        var dependsOn = await PropertyAsync(path, "A", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.DoesNotContain("B", dependsOn!.Choices);
        Assert.DoesNotContain("C", dependsOn.Choices);
    }

    [Fact]
    public async Task TheFirstStage_HasNothingToChooseAndSaysSo()
    {
        // Arrange: a list with no options is a control that does nothing, so it is shown with a
        // reason instead - what it waits for is still the most useful thing on the panel.
        var path = Write(ThreeStages);

        // Act.
        var dependsOn = await PropertyAsync(path, "A", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.False(dependsOn!.IsEditable);
        Assert.Contains("nothing before this", dependsOn.ReadOnlyReason);
    }

    [Fact]
    public async Task TheListSeparatesWaitingForNothingFromTheDefaultOrder()
    {
        // Arrange: those two are the whole subtlety of stage ordering - `dependsOn: []` runs it
        // immediately, no dependsOn at all runs it after the stage before. A list showing both
        // side by side is the clearest place a user will ever meet the difference.
        var path = Write(ThreeStages);

        // Act.
        var dependsOn = await PropertyAsync(path, "C", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Contains(PipelineContextPropertyProvider.NothingCandidate, dependsOn!.Choices);
        Assert.Contains(PipelineContextPropertyProvider.DefaultCandidate, dependsOn.Choices);
    }

    [Fact]
    public async Task PickingAName_MakesTheElementWaitForIt()
    {
        // Arrange & act.
        var path = Write(ThreeStages);
        var result = await SetAsync(path, "C", PipelineContextPropertyProvider.DependsOnPropertyId, "A");

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["A"], _store.GetOrLoad(_workspace, path).Model.Stages.Single(stage => stage.Name == "C").DependsOn);
    }

    [Fact]
    public async Task PickingNothing_WritesAnEmptyDependsOn()
    {
        // Arrange: a real instruction meaning "start immediately", not an absence.
        var path = Write(ThreeStages);

        // Act.
        var result = await SetAsync(
            path,
            "C",
            PipelineContextPropertyProvider.DependsOnPropertyId,
            PipelineContextPropertyProvider.NothingCandidate);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var stage = _store.GetOrLoad(_workspace, path).Model.Stages.Single(candidate => candidate.Name == "C");
        Assert.True(stage.DependsOnDeclared);
        Assert.Empty(stage.DependsOn);
    }

    [Fact]
    public async Task PickingTheDefaultOrder_RemovesTheKeyEntirely()
    {
        // Arrange: the other of the two, and the one that cannot be expressed by any name.
        var path = Write(ThreeStages);
        await SetAsync(path, "C", PipelineContextPropertyProvider.DependsOnPropertyId, "A");

        // Act.
        var result = await SetAsync(
            path,
            "C",
            PipelineContextPropertyProvider.DependsOnPropertyId,
            PipelineContextPropertyProvider.DefaultCandidate);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_store.GetOrLoad(_workspace, path).Model.Stages.Single(stage => stage.Name == "C").DependsOnDeclared);
    }

    [Fact]
    public async Task AnElementWaitingForSeveralThings_IsShownButNotOfferedAsAPicker()
    {
        // Arrange: a single-valued editor cannot express a fan-in, and quietly reducing one to a
        // single dependency would be a destructive edit disguised as a selection. Requirement
        // 13.14 declines to invent a multi-value encoding, so this is shown with a reason.
        var path = Write("""
            stages:
              - stage: A
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: B
                dependsOn: []
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: C
                dependsOn:
                  - A
                  - B
                jobs:
                  - job: J
                    steps:
                      - script: x
            """);

        // Act.
        var dependsOn = await PropertyAsync(path, "C", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Equal("A, B", dependsOn!.Value);
        Assert.False(dependsOn.IsEditable);
        Assert.Contains("several things", dependsOn.ReadOnlyReason);
    }

    [Fact]
    public async Task AJobsCandidates_AreItsSiblingsAndNotItself()
    {
        // Arrange: jobs may depend on any other job in the stage regardless of declared order,
        // which is not true of stages - so the two levels build their lists differently.
        var path = Write("""
            stages:
              - stage: Build
                jobs:
                  - job: First
                    steps:
                      - script: x
                  - job: Second
                    steps:
                      - script: x
            """);

        // Act.
        var dependsOn = await PropertyAsync(path, "Build/First", PipelineContextPropertyProvider.DependsOnPropertyId);

        // Assert.
        Assert.Contains("Second", dependsOn!.Choices);
        Assert.DoesNotContain("First", dependsOn.Choices);
    }

    [Fact]
    public async Task OnlyAChoiceCarriesCandidates()
    {
        // Arrange: an empty repeated field costs nothing, but a Line row carrying options would
        // be a contract nobody meant.
        var path = Write(ThreeStages);

        // Act.
        var properties = await DescribeAsync(path, "C");

        // Assert.
        Assert.All(
            properties.Where(property => property.Editor != ContextPropertyEditor.Choice),
            property => Assert.Empty(property.Choices));
    }

    [Fact]
    public async Task ADependsOnNamingSomethingUnknown_IsRefusedAndTheFileIsUnchanged()
    {
        // Arrange: Requirement 13.11 - the reason comes back for the panel to show, and the
        // document is not written. This is why the missing Choice editor is an ergonomics gap
        // rather than a safety one.
        var path = Write();
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var result = await SetAsync(path, "Test", PipelineContextPropertyProvider.DependsOnPropertyId, "Imaginary");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("Imaginary", result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADependsOnThatWouldCreateACycle_IsRefused()
    {
        // Arrange & act.
        var path = Write();
        var result = await SetAsync(path, "Build", PipelineContextPropertyProvider.DependsOnPropertyId, "Test");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already waiting for it", result.Error);
    }

    [Fact]
    public async Task SettingAPropertyThisElementDoesNotHave_IsRefused()
    {
        // Arrange: a step has no dependsOn - steps run in order.
        var path = Write();

        // Act.
        var result = await SetAsync(path, "Build/Compile/step-0", PipelineContextPropertyProvider.DependsOnPropertyId, "Test");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot be set", result.Error);
    }

    [Fact]
    public async Task AnElementThatIsGone_HasNoProperties()
    {
        // Arrange & act.
        var path = Write();
        var properties = await DescribeAsync(path, "NoSuchStage");

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_HasNoProperties()
    {
        // Arrange & act.
        var path = Write("stages:\n  - stage: Build\n   jobs: [\n");
        var properties = await DescribeAsync(path, "Build");

        // Assert.
        Assert.Empty(properties);
    }

    /// <summary>The id the canvas gives the Build -> Test arrow, from the module's own graph.</summary>
    private string ArrowId(string path)
    {
        var model = _store.GetOrLoad(_workspace, path).Model;
        return PipelineElementMapper.EdgeId(PipelineGraphBuilder.OfStages(model).Edges.Single(edge => edge.FromId == "Build" && edge.ToId == "Test"));
    }

    [Fact]
    public async Task ASelectedArrow_DescribesNothing_AndDoesNotThrow()
    {
        // Arrange: an arrow is selectable now (centralized-selection 2.1), so the property grid is
        // asked about one. PipelineEdits.Locate knows no arrow; the answer must be "nothing to
        // show", never an exception that takes the grid down.
        var path = Write();

        // Act.
        var properties = await DescribeAsync(path, ArrowId(path));

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task AStepTheCanvasDrew_CanBeSelectedAndDescribed()
    {
        // Arrange: the whole path, end to end - a job is opened, the projection emits its steps,
        // and the id it emitted resolves to the rows Requirement 13.4 promises. The step id comes
        // from the projection rather than being written here on purpose: describing a hand-written
        // "Build/Compile/step-0" already passed before a step was ever drawn, so it could not tell
        // whether the canvas could reach one. This fails at "there is a step to describe" until
        // the third level exists.
        var path = Write();
        var model = _store.GetOrLoad(_workspace, path).Model;
        var mapper = new PipelineElementMapper(PipelineMetrics.Default);
        var opened = new HashSet<string>(StringComparer.Ordinal) { "Build", "Build/Compile" };

        // Act.
        var drawn = mapper.Visible(model, new DiagramViewport(0, 0, 0, 0), opened);
        var step = Assert.Single(drawn, element => element.Type == PipelineElementMapper.StepType);
        var properties = await DescribeAsync(path, step.Id);

        // Assert: the rows the requirement names, on the element the canvas actually has.
        Assert.Equal("Script", properties.Single(property => property.Label == "Kind").Value);
        Assert.Contains("dotnet build", properties.Single(property => property.Id == "azure-pipeline.identifier").Value);
    }
}
