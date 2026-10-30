using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// One body in a folder of its own and the module's commands to edit it with, dispatched as the host dispatches them, so
/// that a test of what an edit writes runs the path the product runs (databricks-disl-fbl task 12, Requirement 6).
/// </summary>
internal sealed class DatabricksEditing : IDisposable
{
    private readonly TestFolder _folder;
    private readonly ServiceProvider _provider;
    private readonly ICommandDispatcher _dispatcher;

    private DatabricksEditing(string name, byte[] bytes)
    {
        _folder = TestFolder.Create("databricks-editing");
        Body = _folder.Combine(name);
        File.WriteAllBytes(Body, bytes);
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        _dispatcher = _provider.GetRequiredService<ICommandDispatcher>();
    }

    /// <summary>The body's path.</summary>
    public string Body { get; }

    /// <summary>A copy of the fixture <paramref name="fixture"/>, byte for byte.</summary>
    public static DatabricksEditing Of(string fixture) =>
        new(fixture, File.ReadAllBytes(Fixture(fixture)));

    /// <summary>A copy of the file at <paramref name="path"/>, byte for byte, under its own name.</summary>
    public static DatabricksEditing Copying(string path) =>
        new(IoPath.GetFileName(path), File.ReadAllBytes(path));

    /// <summary>A body named <paramref name="name"/> holding <paramref name="text"/>.</summary>
    public static DatabricksEditing Holding(string name, string text) => new(name, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>The path of the fixture <paramref name="name"/> beside the test binary.</summary>
    public static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Dispatches the command <paramref name="command"/> makes for the body.</summary>
    public Task<CommandResult> RunAsync(Func<string, ICommand> command) =>
        _dispatcher.DispatchAsync(command(Body), TestContext.Current.CancellationToken);

    /// <summary>Dispatches the inverse of <paramref name="result"/>.</summary>
    public Task<CommandResult> UndoAsync(CommandResult result) =>
        _dispatcher.DispatchAsync(result.Inverse!, TestContext.Current.CancellationToken);

    /// <summary>The body's text as it is on disk now.</summary>
    public Task<string> TextAsync() => File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);

    /// <summary>The body's bytes as they are on disk now.</summary>
    public Task<byte[]> BytesAsync() => File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);

    /// <summary>The body as the module reads it now.</summary>
    public async Task<DatabricksModel> ModelAsync() => DatabricksModel.Read(LineDocument.Parse(await TextAsync()));

    /// <summary>The body's one job.</summary>
    public async Task<JobModel> JobAsync() => Assert.Single((await ModelAsync()).Jobs);

    /// <summary>The body's one pipeline.</summary>
    public async Task<PipelineModel> PipelineAsync() => Assert.Single((await ModelAsync()).Pipelines);

    /// <summary>The task keyed <paramref name="key"/>, in whichever job holds it.</summary>
    public async Task<JobTask> TaskAsync(string key) =>
        (await ModelAsync()).Jobs.SelectMany(job => job.Tasks).Single(task => task.Key == key);

    public void Dispose()
    {
        _provider.Dispose();
        _folder.Dispose();
    }
}
