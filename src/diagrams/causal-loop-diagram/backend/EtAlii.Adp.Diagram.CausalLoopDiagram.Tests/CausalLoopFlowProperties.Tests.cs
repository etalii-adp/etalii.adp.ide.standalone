using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// The flow properties of a causal loop diagram (etalii.adp spec 074, User Story 8): the diagram's own rows with nothing
/// selected, a variable's two rows, each edit written as <c>contracts/cld-flow-statements.md</c> gives it, one step of undo,
/// and a file that states only what differs from the defaults.
/// </summary>
public class CausalLoopFlowPropertiesTests : IDisposable
{
    private const string Corpus =
        "causal-loop 1\r\n\r\nvariable a \"A\"\r\nvariable b B\r\n\r\n# Reinforcing.\r\nlink a -> b + \"a note\"\r\nlink b -> a + delayed\r\n\r\nloop R1 \"Round\" a b\r\n";

    private readonly TestFolder _testFolder = TestFolder.Create("causal-loop-flow-properties");
    private readonly string _path;
    private readonly ServiceProvider _provider;
    private readonly CausalLoopDocumentStore _store = new(new FileWriter());
    private readonly CausalLoopDiagramPropertyProvider _diagram;
    private readonly CausalLoopContextPropertyProvider _elements;

    public CausalLoopFlowPropertiesTests()
    {
        _path = IoPath.Combine(_testFolder.Path, "feedback.cld");
        _provider = new ServiceCollection()
            .AddSingleton<ICausalLoopDocumentStore>(_store)
            .AddCommands().AddHierarchyCommandHandlers().AddCausalLoop().BuildServiceProvider();
        var historyStacks = _provider.GetRequiredService<IHistoryStackStore>();
        _diagram = new CausalLoopDiagramPropertyProvider(historyStacks, _store, () => new DiagramFileRouter(new CausalLoopOnlyCatalog()));
        _elements = new CausalLoopContextPropertyProvider(historyStacks, _store);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
        GC.SuppressFinalize(this);
    }

    private ContextTarget DiagramTarget() =>
        new(ContextScope.Hierarchy, _path, false, ShortGuid.NewShortGuid(), ProjectRoot.From(_testFolder.Path));

    private ContextTarget ElementTarget(string elementId) =>
        new(ContextScope.DiagramElement, _path, false, ShortGuid.NewShortGuid(), ProjectRoot.From(_testFolder.Path), default, elementId);

    private async Task Write(string text) => await File.WriteAllTextAsync(_path, text, TestContext.Current.CancellationToken);

    private async Task<string> Text() => await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);

    private async Task<string> Set(string property, string value)
    {
        var result = await _diagram.SetAsync(DiagramTarget(), property, value, TestContext.Current.CancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    private async Task<string> SetOn(string element, string property, string value)
    {
        var result = await _elements.SetAsync(ElementTarget(element), property, value, TestContext.Current.CancellationToken);
        return result.IsSuccess ? "" : result.Error;
    }

    private async Task<string> Shown(string property) =>
        (await _diagram.DescribeAsync(DiagramTarget(), TestContext.Current.CancellationToken)).Single(row => row.Id == property).Value;

    private async Task<string> ShownOn(string element, string property) =>
        (await _elements.DescribeAsync(ElementTarget(element), TestContext.Current.CancellationToken)).Single(row => row.Id == property).Value;

    [Fact]
    public async Task TheDiagramsRows_ShowTheirDefaults_WhereTheFileStatesNone()
    {
        // Arrange.
        await Write(Corpus);

        // Act.
        var rows = await _diagram.DescribeAsync(DiagramTarget(), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
        [
            "Identity | Title |  | causal-loop.diagram-title",
            "Flow page | Link strength | 3 | causal-loop.flow-strength",
            "Flow page | Delay length | 5 | causal-loop.flow-delay",
            "Flow page | Inertia | 1.2 | causal-loop.flow-inertia",
            "Flow page | Simulation speed | 1 | causal-loop.flow-speed",
            "Flow page | Dot density | 3 | causal-loop.flow-density",
            "Flow page | Heatmap reading | Backlog | causal-loop.flow-heatmap",
        ], rows.Select(row => $"{row.Group} | {row.Label} | {row.Value} | {row.Id}"));
    }

    [Fact]
    public async Task AVariable_ShowsItsStartingLevelAndOutsidePush()
    {
        // Arrange.
        await Write(Corpus);

        // Act and assert.
        Assert.Equal("0.5", await ShownOn("variable:a", "causal-loop.variable-start"));
        Assert.Equal("0", await ShownOn("variable:a", "causal-loop.variable-push"));
    }

    [Fact]
    public async Task ATitle_GoesRightAfterTheHeader_AndAFlowStatementAfterTheTitle()
    {
        // Arrange.
        await Write(Corpus);

        // Act.
        Assert.Equal("", await Set("causal-loop.flow-strength", "4"));
        var first = await Text();
        Assert.Equal("", await Set("causal-loop.diagram-title", "Round and round"));

        // Assert.
        Assert.Equal(Corpus.Replace("causal-loop 1\r\n", "causal-loop 1\r\nflow strength=4\r\n", StringComparison.Ordinal), first);
        Assert.Equal(Corpus.Replace("causal-loop 1\r\n", "causal-loop 1\r\ntitle \"Round and round\"\r\nflow strength=4\r\n", StringComparison.Ordinal), await Text());
        Assert.Equal("Round and round", await Shown("causal-loop.diagram-title"));
        Assert.Equal("4", await Shown("causal-loop.flow-strength"));
    }

    [Fact]
    public async Task AnEdit_IsOneStepOfUndo()
    {
        // Arrange.
        await Write(Corpus);
        Assert.Equal("", await Set("causal-loop.flow-delay", "6.5"));

        // Act.
        await _provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path)).UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Corpus, await Text());
        Assert.Equal("5", await Shown("causal-loop.flow-delay"));
    }

    [Fact]
    public async Task EveryWordOfTheFlowStatement_IsKept_WhenAnotherIsSet()
    {
        // Arrange.
        await Write(Corpus);

        // Act.
        Assert.Equal("", await Set("causal-loop.flow-heatmap", "Pressure"));
        Assert.Equal("", await Set("causal-loop.flow-strength", "4"));
        Assert.Equal("", await Set("causal-loop.flow-delay", "6.5"));
        Assert.Equal("", await Set("causal-loop.flow-inertia", "2"));
        Assert.Equal("", await Set("causal-loop.flow-speed", "0.5"));
        Assert.Equal("", await Set("causal-loop.flow-density", "1.5"));

        // Assert.
        Assert.Equal(
            Corpus.Replace("causal-loop 1\r\n", "causal-loop 1\r\nflow strength=4 delay=6.5 inertia=2 speed=0.5 density=1.5 heatmap=pressure\r\n", StringComparison.Ordinal),
            await Text());
        Assert.Equal("Pressure", await Shown("causal-loop.flow-heatmap"));
    }

    [Fact]
    public async Task APropertySetBackToItsDefault_LeavesTheFile_AndAnEmptyStatementGoesWithIt()
    {
        // Arrange.
        await Write(Corpus);
        Assert.Equal("", await Set("causal-loop.diagram-title", "T"));
        Assert.Equal("", await Set("causal-loop.flow-strength", "4"));
        Assert.Equal("", await Set("causal-loop.flow-delay", "6.5"));

        // Act and assert.
        Assert.Equal("", await Set("causal-loop.flow-strength", "3"));
        Assert.Equal(Corpus.Replace("causal-loop 1\r\n", "causal-loop 1\r\ntitle \"T\"\r\nflow delay=6.5\r\n", StringComparison.Ordinal), await Text());
        Assert.Equal("", await Set("causal-loop.flow-delay", ""));
        Assert.Equal(Corpus.Replace("causal-loop 1\r\n", "causal-loop 1\r\ntitle \"T\"\r\n", StringComparison.Ordinal), await Text());
        Assert.Equal("", await Set("causal-loop.diagram-title", ""));
        Assert.Equal(Corpus, await Text());
    }

    [Fact]
    public async Task AVariablesWords_AreWrittenAfterItsLabel_AndNothingElseMoves()
    {
        // Arrange.
        await Write(Corpus);

        // Act.
        Assert.Equal("", await SetOn("variable:a", "causal-loop.variable-push", "-1.5"));
        Assert.Equal("", await SetOn("variable:a", "causal-loop.variable-start", "0.7"));
        Assert.Equal("", await SetOn("variable:b", "causal-loop.variable-start", "0.3"));

        // Assert.
        Assert.Equal(
            Corpus.Replace("variable a \"A\"", "variable a \"A\" start=0.7 push=-1.5", StringComparison.Ordinal)
                .Replace("variable b B", "variable b B start=0.3", StringComparison.Ordinal),
            await Text());

        // Act and assert: back to their defaults.
        Assert.Equal("", await SetOn("variable:a", "causal-loop.variable-start", "0.5"));
        Assert.Equal("", await SetOn("variable:a", "causal-loop.variable-push", "0"));
        Assert.Equal("", await SetOn("variable:b", "causal-loop.variable-start", ""));
        Assert.Equal(Corpus, await Text());
    }

    [Fact]
    public async Task AVariableWithoutALabel_KeepsItsWords_WhenItGetsOne()
    {
        // Arrange.
        await Write("causal-loop 1\r\nvariable a start=0.7\r\n");
        Assert.Equal("", await ShownOn("variable:a", "causal-loop.variable-label"));
        Assert.Equal("0.7", await ShownOn("variable:a", "causal-loop.variable-start"));

        // Act.
        Assert.Equal("", await SetOn("variable:a", "causal-loop.variable-label", "A"));

        // Assert.
        Assert.Equal("causal-loop 1\r\nvariable a \"A\" start=0.7\r\n", await Text());
    }

    [Fact]
    public async Task AValueOutOfRange_IsRefusedWithTheRange_AndWritesNothing()
    {
        // Arrange.
        await Write(Corpus);

        // Act and assert.
        Assert.Equal("Link strength is 9; it must be between 0.5 and 8.", await Set("causal-loop.flow-strength", "9"));
        Assert.Equal("Starting level is 2; it must be between 0 and 1.", await SetOn("variable:a", "causal-loop.variable-start", "2"));
        Assert.Equal("Delay length is wide; it must be between 1 and 12.", await Set("causal-loop.flow-delay", "wide"));
        Assert.NotEqual("", await Set("causal-loop.diagram-title", "say \"so\""));
        Assert.Equal(Corpus, await Text());
    }

    [Fact]
    public void AFileHoldingAValueOutOfRange_OpensWithFindings_AndKeepsItsBytes()
    {
        // Arrange.
        const string text = "causal-loop 1\r\nflow strength=9\r\nvariable a \"A\" start=2\r\n";

        // Act.
        var body = CausalLoopBody.Parse(text);
        var findings = CausalLoopDefinition.Findings(body).Select(finding => finding.Message).ToList();

        // Assert.
        Assert.Contains("Link strength is 9; it must be between 0.5 and 8.", findings);
        Assert.Contains("Starting level is 2; it must be between 0 and 1.", findings);
        Assert.Equal(text, body.Text);
    }

    [Fact]
    public async Task AFlowStatementWithAWordItCannotRead_IsKeptAndReported()
    {
        // Arrange.
        const string text = "causal-loop 1\r\nflow strength=4 sparkle=2\r\nflow heatmap=hot\r\nvariable a\r\n";
        await Write(text);

        // Act.
        var findings = CausalLoopDefinition.Findings(CausalLoopBody.Parse(text)).Select(finding => finding.Message).ToList();

        // Assert.
        Assert.Equal(2, findings.Count(message => message == "'flow' is not a statement this reading knows."));
        Assert.Equal("3", await Shown("causal-loop.flow-strength"));
    }

    [Fact]
    public async Task OfTwoFlowStatements_TheLastThatStatesAKeyWins_AndAnEditGoesThere()
    {
        // Arrange.
        await Write("causal-loop 1\r\nflow strength=4 delay=2\r\nflow strength=5\r\n");
        Assert.Equal("5", await Shown("causal-loop.flow-strength"));
        Assert.Equal("2", await Shown("causal-loop.flow-delay"));

        // Act and assert.
        Assert.Equal("", await Set("causal-loop.flow-strength", "6"));
        Assert.Equal("causal-loop 1\r\nflow strength=4 delay=2\r\nflow strength=6\r\n", await Text());
        Assert.Equal("", await Set("causal-loop.flow-delay", "5"));
        Assert.Equal("causal-loop 1\r\nflow strength=4\r\nflow strength=6\r\n", await Text());

        // A property back at its default leaves every statement that states it, and both go.
        Assert.Equal("", await Set("causal-loop.flow-strength", "3"));
        Assert.Equal("causal-loop 1\r\n", await Text());
    }

    [Fact]
    public async Task AFileWithoutAHeader_TakesTheNewStatementFirst()
    {
        // Arrange.
        await Write("variable a\r\n");

        // Act.
        Assert.Equal("", await Set("causal-loop.flow-strength", "4"));

        // Assert.
        Assert.Equal("flow strength=4\r\nvariable a\r\n", await Text());
    }

    [Fact]
    public async Task AHeaderWithoutALineEnding_IsGivenOneBeforeTheNewStatement()
    {
        // Arrange.
        await Write("causal-loop 1");

        // Act.
        Assert.Equal("", await Set("causal-loop.diagram-title", "T"));

        // Assert.
        Assert.Equal("causal-loop 1\r\ntitle \"T\"", await Text());
    }
}
