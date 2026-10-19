using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The four change and deletion rules of the bundled definition that the module worked around until they were
/// corrected in etalii.adp (<c>noCycleByEdit</c>, <c>keepAStart</c>, <c>keepTheOnlyStart</c>, and the sentences of
/// <c>keepLastJob</c> and <c>keepLastStep</c>), now deciding on their own: each case is run through the command and
/// through <see cref="HandWrittenPipelineEdits"/>, and the two must refuse with the same sentence or both write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seven were seen to fail</b> against the definition as bundled before the correction (etalii.adp
/// <c>9a1ce2cfa0b9a5197ddc52f3fdf6cba29f609e8e</c>, with its provenance) and the module's workarounds removed, each
/// on the outcome or the sentence it names. The other three are guards that the corrected rules still refuse what
/// they are for, or still allow what was always allowed, and pass against either.
/// </para>
/// <para>
/// <b>One case differs from the hand-written edit on purpose</b>: removing the one stage that waits for nothing, where
/// the stages left would all wait. The hand-written check of it was passed no dependencies and returned early, so it
/// never refused; the definition refuses with the sentence that check was written to give.
/// </para>
/// </remarks>
public class PipelineEditRulesTests
{
    private const string Sequential = """
        stages:
          - stage: A
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: B
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

    private const string ChainedJobs = """
        stages:
          - stage: A
            jobs:
              - job: X
                steps:
                  - script: x
              - job: Y
                dependsOn: X
                steps:
                  - script: x
              - job: Z
                dependsOn: Y
                steps:
                  - script: x
        """;

    private const string FirstOptsOut = """
        stages:
          - stage: A
            dependsOn: []
            jobs:
              - job: J
                steps:
                  - script: x
          - stage: B
            jobs:
              - job: J
                steps:
                  - script: x
        """;

    private const string Unnamed = """
        stages:
          - stage: A
            jobs:
              - job: J
                steps:
                  - script: x
          - jobs:
              - steps:
                  - script: x
                  - script: y
        """;

    private const string UnnamedJob = """
        stages:
          - stage: A
            jobs:
              - job: J
                steps:
                  - script: x
              - steps:
                  - script: x
        """;

    [Fact]
    public async Task WaitingForWhatTheElementAlreadyWaitsFor_IsWritten()
    {
        // Act: C already waits for A, through B; waiting for it directly closes nothing (noCycleByEdit).
        var outcome = await Run(Sequential, workspace => new SetPipelineDependenciesCommand(ProjectRoot.From(workspace.Root), workspace.Body, "C", ["A"], true), (text, model) => HandWrittenPipelineEdits.SetDependencies(text, model, "C", ["A"], true));

        // Assert.
        Assert.Equal("written", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task WaitingForWhatWaitsForTheElement_IsRefused()
    {
        // Act: Z waits for X, through Y; X waiting for Z closes a cycle (noCycleByEdit).
        var outcome = await Run(ChainedJobs, workspace => new SetPipelineDependenciesCommand(ProjectRoot.From(workspace.Root), workspace.Body, "A/X", ["Z"], true), (text, model) => HandWrittenPipelineEdits.SetDependencies(text, model, "A/X", ["Z"], true));

        // Assert.
        Assert.Equal("refused: That would make X wait for something that is already waiting for it.", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task AnUnnamedStageWaitingForWhatWaitsForIt_IsRefusedCallingItAStage()
    {
        // Act: B follows the unnamed stage-0 by the default order (noCycleByEdit's sentence for an element with no label).
        const string unnamedFirst = """
            stages:
              - jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: B
                jobs:
                  - job: J
                    steps:
                      - script: x
            """;
        var outcome = await Run(unnamedFirst, workspace => new SetPipelineDependenciesCommand(ProjectRoot.From(workspace.Root), workspace.Body, "stage-0", ["B"], true), (text, model) => HandWrittenPipelineEdits.SetDependencies(text, model, "stage-0", ["B"], true));

        // Assert.
        Assert.Equal("refused: That would make stage wait for something that is already waiting for it.", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task ClearingTheDependenciesOfTheOnlyStartingStage_IsWritten()
    {
        // Act: A is the one stage that waits for nothing; back to the default order it is the first stage still (keepAStart).
        var outcome = await Run(FirstOptsOut, workspace => new SetPipelineDependenciesCommand(ProjectRoot.From(workspace.Root), workspace.Body, "A", [], false), (text, model) => HandWrittenPipelineEdits.SetDependencies(text, model, "A", [], false));

        // Assert.
        Assert.Equal("written", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task GivingTheOnlyStartingStageADependency_IsRefused()
    {
        // Arrange: B and C wait for each other, so A is the one stage that waits for nothing, and waiting for B closes no cycle.
        const string pipeline = """
            stages:
              - stage: A
                dependsOn: []
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: B
                dependsOn: C
                jobs:
                  - job: J
                    steps:
                      - script: x
              - stage: C
                dependsOn: B
                jobs:
                  - job: J
                    steps:
                      - script: x
            """;

        // Act: keepAStart still refuses what it is for.
        var outcome = await Run(pipeline, workspace => new SetPipelineDependenciesCommand(ProjectRoot.From(workspace.Root), workspace.Body, "A", ["B"], true), (text, model) => HandWrittenPipelineEdits.SetDependencies(text, model, "A", ["B"], true));

        // Assert.
        Assert.Equal("refused: A pipeline must contain at least one stage with no dependencies, or it has nothing to start with.", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task RemovingTheFirstOfStagesInTheDefaultOrder_IsWritten()
    {
        // Act: B becomes the first stage, and so the one to start with (keepTheOnlyStart).
        var outcome = await Run(Sequential, workspace => new RemovePipelineElementCommand(ProjectRoot.From(workspace.Root), workspace.Body, "A"), (text, model) => HandWrittenPipelineEdits.Remove(text, model, "A"));

        // Assert.
        Assert.Equal("written", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task RemovingTheStageThatLeavesNoneToStartWith_IsRefused()
    {
        // Arrange: B is the one stage that waits for nothing; without it C follows A by the default order, and A waits for C.
        const string pipeline = """
            stages:
              - stage: A
                dependsOn: C
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

        // Act.
        var outcome = await Run(pipeline, workspace => new RemovePipelineElementCommand(ProjectRoot.From(workspace.Root), workspace.Body, "B"), (text, model) => HandWrittenPipelineEdits.Remove(text, model, "B"));

        // Assert: the hand-written check of this refusal was passed no dependencies and so never refused (it returns
        // early on an empty list); the definition refuses with the sentence that check was written to give.
        Assert.Equal("written", outcome.HandWritten);
        Assert.Equal("refused: A pipeline must contain at least one stage with no dependencies, and this is the only one.", outcome.Definition);
    }

    [Fact]
    public async Task RemovingTheLastJobOfAnUnnamedStage_IsRefusedCallingItAStage()
    {
        // Act: keepLastJob's sentence for a stage with no label.
        const string pipeline = """
            stages:
              - stage: A
                jobs:
                  - job: J
                    steps:
                      - script: x
              - jobs:
                  - job: Only
                    steps:
                      - script: x
            """;
        var outcome = await Run(pipeline, workspace => new RemovePipelineElementCommand(ProjectRoot.From(workspace.Root), workspace.Body, "stage-1/Only"), (text, model) => HandWrittenPipelineEdits.Remove(text, model, "stage-1/Only"));

        // Assert.
        Assert.Equal("refused: stage needs at least one job.", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task RemovingTheLastStepOfAnUnnamedJob_IsRefusedCallingItAJob()
    {
        // Act: keepLastStep's sentence for a job with no label.
        var outcome = await Run(UnnamedJob, workspace => new RemovePipelineElementCommand(ProjectRoot.From(workspace.Root), workspace.Body, "A/job-1/step-0"), (text, model) => HandWrittenPipelineEdits.Remove(text, model, "A/job-1/step-0"));

        // Assert.
        Assert.Equal("refused: job needs at least one step.", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    [Fact]
    public async Task RemovingAStepOfAnUnnamedStage_IsWritten()
    {
        // Act: the job of the unnamed stage has two steps, so one of them may go.
        var outcome = await Run(Unnamed, workspace => new RemovePipelineElementCommand(ProjectRoot.From(workspace.Root), workspace.Body, "stage-1/job-0/step-0"), (text, model) => HandWrittenPipelineEdits.Remove(text, model, "stage-1/job-0/step-0"));

        // Assert.
        Assert.Equal("written", outcome.HandWritten);
        Assert.Equal(outcome.HandWritten, outcome.Definition);
    }

    /// <summary>Where a case's pipeline is written: the workspace and the body's path in it.</summary>
    private sealed record Workspace(string Root, string Body);

    /// <summary>What the hand-written edit and the command did with one pipeline: "written", or "refused: " and the sentence.</summary>
    private sealed record Outcome(string HandWritten, string Definition);

    /// <summary>Runs <paramref name="command"/> through the dispatcher and <paramref name="handWritten"/> on the same text.</summary>
    private static async Task<Outcome> Run(string pipeline, Func<Workspace, IPipelineEditCommand> command, Func<LineDocument, PipelineModel, string?> handWritten)
    {
        using var folder = TestFolder.Create("EtAlii.Adp.PipelineEditRules");
        var workspace = new Workspace(folder.Path, Path.Combine(folder.Path, "azure-pipelines.yml"));
        await File.WriteAllTextAsync(workspace.Body, pipeline, Encoding.UTF8, TestContext.Current.CancellationToken);

        var text = LineDocument.Parse(pipeline);
        var refusal = handWritten(text, PipelineParser.Parse(text, new PipelineTemplates(ProjectRoot.From(workspace.Root)), workspace.Body));

        var result = await new PipelineTestDispatcher(new PipelineDocumentStore(new FileWriter())).DispatchAsync(command(workspace), TestContext.Current.CancellationToken);
        return new Outcome(
            refusal is null ? "written" : $"refused: {refusal}",
            result.IsSuccess ? "written" : $"refused: {result.Error}");
    }
}
