using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>What every handler does with a save, through the shared contract and for each command of the family (code-structure FR-022).</summary>
public sealed class DatabricksCommandHandlersTests : EditHandlerContract<IDatabricksDocumentStore, LineDocument, DatabricksModel>
{
    private const string SaveWarning = "The handlers' test warning: something beside the file was not kept.";

    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    protected override string Example => Fixture("bundle.yml");

    protected override IDatabricksDocumentStore CreateStore() => new DatabricksDocumentStore(new FileWriter());

    protected override IDatabricksDocumentStore Warning(IDatabricksDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IDatabricksDocumentStore store) =>
        new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .AddSingleton(store)
            .BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

    protected override ICommand EditOf(string bodyPath) => new SetDatabricksBundleNameCommand(bodyPath, "lakehouse-weekly");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameDatabricksTaskCommand(bodyPath, "", "gone", "still-gone");

    /// <summary>One edit of each command, on the fixture it reads.</summary>
    public static TheoryData<string, string> Commands() => new()
    {
        { "add-resource", "bundle.yml" },
        { "bundle-name", "bundle.yml" },
        { "insert-task", "job.yml" },
        { "remove-task", "job.yml" },
        { "connect", "job.yml" },
        { "disconnect", "job.yml" },
        { "rename-task", "job.yml" },
        { "run-if", "job.yml" },
        { "cluster", "job.yml" },
        { "insert-library", "pipeline.json" },
        { "remove-library", "pipeline.json" },
        { "pipeline-scalar", "pipeline.json" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task EveryHandler_CarriesTheSaveWarning_AndCanBeUndone(string edit, string fixture)
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.DatabricksCommandHandlersTests");
        var body = folder.Combine(fixture);
        File.Copy(Fixture(fixture), body);
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

    private static ICommand CommandOf(string edit, string body) => edit switch
    {
        "add-resource" => new AddDatabricksResourceCommand(body, "jobs", "weekly_report"),
        "bundle-name" => new SetDatabricksBundleNameCommand(body, "lakehouse-weekly"),
        "insert-task" => new InsertDatabricksTaskCommand(body, "", "archive", "notebook", "notebooks/archive"),
        "remove-task" => new RemoveDatabricksTaskCommand(body, "", "publish"),
        "connect" => new ConnectDatabricksTasksCommand(body, "", "ingest", "publish"),
        "disconnect" => new DisconnectDatabricksTasksCommand(body, "", "ingest", "quality_gate"),
        "rename-task" => new RenameDatabricksTaskCommand(body, "", "ingest", "load"),
        "run-if" => new SetDatabricksRunIfCommand(body, "", "publish", "ALL_DONE"),
        "cluster" => new SetDatabricksClusterCommand(body, "", "publish", "ingest_cluster"),
        "insert-library" => new InsertDatabricksLibraryCommand(body, "", "notebook", "transformations/platinum"),
        "remove-library" => new RemoveDatabricksLibraryCommand(body, "", "file", "transformations/silver.py"),
        "pipeline-scalar" => new SetDatabricksPipelineScalarCommand(body, "", "schema", "platinum"),
        _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, "No such edit."),
    };

    private sealed class WarningStore(IDatabricksDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, DatabricksModel>(inner, warning), IDatabricksDocumentStore;
}
