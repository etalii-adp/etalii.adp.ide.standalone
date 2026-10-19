using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>What every handler does with a save, through the shared contract and for each command of the module (code-structure FR-022).</summary>
/// <remarks>
/// The contract speaks the shared store interface, which names a path alone, and this module's
/// handlers take a store that is also told the workspace root. So the store the contract hands over
/// is the pipeline store seen from one root, and the handlers reach it through
/// <see cref="ViewedPipelineDocumentStore"/>.
/// </remarks>
public sealed class PipelineCommandHandlersTests : EditHandlerContract<IDiagramDocumentStore<PipelineBody, PipelineModel>, PipelineBody, PipelineModel>
{
    private const string SaveWarning = "The handlers' test warning: something beside the file was not kept.";

    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    protected override string Example => Fixture("multi-stage.yml");

    protected override IDiagramDocumentStore<PipelineBody, PipelineModel> CreateStore() =>
        new PipelineDocumentStore(new FileWriter()).In(ProjectRoot.From(IoPath.GetTempPath()));

    protected override IDiagramDocumentStore<PipelineBody, PipelineModel> Warning(IDiagramDocumentStore<PipelineBody, PipelineModel> store, string warning) =>
        new WarningDocumentStore<PipelineBody, PipelineModel>(store, warning);

    protected override ICommandDispatcher Dispatcher(IDiagramDocumentStore<PipelineBody, PipelineModel> store) =>
        new PipelineTestDispatcher(new ViewedPipelineDocumentStore(store));

    protected override ICommand EditOf(string bodyPath) => new RenamePipelineElementCommand(ProjectRoot.From(RootOf(bodyPath)), bodyPath, "Build", "Build it all");

    protected override ICommand EditOfMissing(string bodyPath) => new RenamePipelineElementCommand(ProjectRoot.From(RootOf(bodyPath)), bodyPath, "NoSuchStage", "Anything");

    /// <summary>One edit of each command of the module.</summary>
    public static TheoryData<string> Commands() =>
    [
        "rename",
        "enabled",
        "dependencies",
        "add-stage",
        "add-job",
        "add-step",
        "remove",
        "remove-stage",
        "move",
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task EveryHandler_CarriesTheSaveWarning_AndCanBeUndone(string edit)
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.PipelineCommandHandlersTests");
        var body = folder.Combine("multi-stage.yml");
        File.Copy(Example, body);
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var dispatcher = Dispatcher(Warning(CreateStore(), SaveWarning));

        // Act.
        var result = await dispatcher.DispatchAsync(CommandOf(edit, body), TestContext.Current.CancellationToken);
        var undone = await dispatcher.DispatchAsync(result.Inverse!, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SaveWarning, result.Warning);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARemovedStep_IsRemovedAgainByTheRedoOfItsUndo()
    {
        // Arrange: a step has no name for the undo to find it by again, so its redo is the text
        // as it was without it.
        using var folder = TestFolder.Create("EtAlii.Adp.PipelineCommandHandlersTests");
        var body = folder.Combine("multi-stage.yml");
        File.Copy(Example, body);
        var dispatcher = Dispatcher(CreateStore());
        var removed = await dispatcher.DispatchAsync(new RemovePipelineElementCommand(ProjectRoot.From(RootOf(body)), body, "Build/Compile/step-1"), TestContext.Current.CancellationToken);
        var without = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var undone = await dispatcher.DispatchAsync(removed.Inverse!, TestContext.Current.CancellationToken);

        // Act.
        Assert.NotNull(undone.Inverse);
        var redone = await dispatcher.DispatchAsync(undone.Inverse, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.Equal(without, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    private static ICommand CommandOf(string edit, string body)
    {
        var root = RootOf(body);
        return edit switch
        {
            "rename" => new RenamePipelineElementCommand(ProjectRoot.From(root), body, "Build", "Build it all"),
            "enabled" => new SetPipelineElementEnabledCommand(ProjectRoot.From(root), body, "Test", Enabled: false),
            "dependencies" => new SetPipelineDependenciesCommand(ProjectRoot.From(root), body, "DeployProduction", ["Build"], Declared: true),
            "add-stage" => new AddPipelineElementCommand(ProjectRoot.From(root), body, PipelineAddKind.Stage, ""),
            "add-job" => new AddPipelineElementCommand(ProjectRoot.From(root), body, PipelineAddKind.Job, "Test"),
            "add-step" => new AddPipelineElementCommand(ProjectRoot.From(root), body, PipelineAddKind.Step, "Build/Compile"),
            "remove" => new RemovePipelineElementCommand(ProjectRoot.From(root), body, "Test/Integration"),
            "remove-stage" => new RemovePipelineElementCommand(ProjectRoot.From(root), body, "Test"),
            "move" => new MovePipelineStepCommand(ProjectRoot.From(root), body, "Build/Compile/step-0", 2),
            _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit."),
        };
    }

    private static string RootOf(string body) => IoPath.GetDirectoryName(body)!;
}
