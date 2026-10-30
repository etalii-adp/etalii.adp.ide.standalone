using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// One body, registered twice as two tool types (databricks-disl-fbl Requirement 4.5, FBL 9.1): the bundle's reading and
/// a job's reading of one <c>databricks.yml</c> are both editable, an edit made through one shows in the other without a
/// reload, one undo history serves both, and every stored position applies with no registration rewritten.
/// </summary>
public sealed class DatabricksTwoReadingsTests : IDisposable
{
    private const string Fixture = "two-jobs-two-pipelines.yml";

    private readonly TestFolder _testFolder = TestFolder.Create("databricks-two-readings");
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextActionProvider _actions;
    private readonly DatabricksContextPropertyProvider _properties;
    private readonly string _body;

    public DatabricksTwoReadingsTests()
    {
        _root = _testFolder.Path;
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        var history = _provider.GetRequiredService<IHistoryStackStore>();
        var documents = _provider.GetRequiredService<IDatabricksDocumentStore>();
        _actions = new DatabricksContextActionProvider(history, documents);
        _properties = new DatabricksContextPropertyProvider(history, documents);
        _body = IoPath.Combine(_root, Fixture);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), _body);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task EveryStoredPosition_AppliesInEachReading_AndNoRegistrationIsRewritten()
    {
        // Arrange.
        var bundle = Registration("bundle.adp", "databricks/bundle", "", "layout:\r\n  resource:jobs/job_b: 701 702\r\n");
        var job = Registration("job_b.adp", "databricks/job", "resource: job_b\r\n", "layout:\r\n  task:report: 501 502\r\n");
        var before = await BytesAsync(bundle, job);

        // Act.
        await using var bundleSession = Open(ServiceCollectionAddDatabricksExtension.BundleOrigin, bundle);
        await using var jobSession = Open(ServiceCollectionAddDatabricksExtension.JobOrigin, job);
        var bundleElements = await bundleSession.BaselineElementsAsync(TestContext.Current.CancellationToken);
        var jobElements = await jobSession.BaselineElementsAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal((701d, 702d), Position(bundleElements, "resource:jobs/job_b"));
        Assert.Equal((501d, 502d), Position(jobElements, "task:report"));
        Assert.Equal(before, await BytesAsync(bundle, job));
    }

    [Fact]
    public async Task AnEditThroughTheBundle_ShowsInTheJob_AndOneHistoryUndoesEditsOfBoth()
    {
        // Arrange: the job registration names a job the file does not declare yet, so its reading starts empty.
        var bundle = Registration("bundle.adp", "databricks/bundle", "", "");
        var job = Registration("job_c.adp", "databricks/job", "resource: job_c\r\n", "");
        var original = await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken);
        await using var jobSession = Open(ServiceCollectionAddDatabricksExtension.JobOrigin, job);
        Assert.Empty(await ElementsAsync(jobSession));

        // Act: the bundle adds the job; then the job's own reading edits the task it was given.
        var added = await _actions.CommitAsync(
            Target("bundle", ServiceCollectionAddDatabricksExtension.BundleOrigin, bundle),
            $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", "job_c", "", TestContext.Current.CancellationToken);
        var shown = await ElementsAsync(jobSession);
        var task = Assert.Single(shown, element => element.Id.StartsWith("task:", StringComparison.Ordinal)).Id;
        var edited = await _properties.SetAsync(
            Target(task, ServiceCollectionAddDatabricksExtension.JobOrigin, job),
            DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", TestContext.Current.CancellationToken);

        // Assert: both edits are in the one body, the job's on top of the bundle's.
        Assert.True(added.Completed, added.Error);
        Assert.True(edited.IsSuccess, edited.Error);
        var model = await ReadAsync();
        Assert.Equal(["job_a", "job_b", "job_c"], model.Bundle.Resources.Where(resource => resource.Kind == "jobs").Select(resource => resource.Key));
        Assert.Equal("ALL_DONE", model.Jobs.Single(candidate => candidate.Key == "job_c").Tasks.Single().RunIf);

        // Assert: one history serves both readings, the job's edit first, then the bundle's.
        var history = _provider.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_root));
        await history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal("", (await ReadAsync()).Jobs.Single(candidate => candidate.Key == "job_c").Tasks.Single().RunIf);
        await history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, await File.ReadAllBytesAsync(_body, TestContext.Current.CancellationToken));
        Assert.Empty(await ElementsAsync(jobSession));
    }

    private IDiagramSession Open(ToolOrigin origin, string registration)
    {
        var factory = _provider.GetServices<IDiagramSessionFactory>().Single(candidate => candidate.Origin.Key == origin.Key);
        return factory.Open(ShortGuid.NewShortGuid(), ProjectRoot.From(_root), _body, registration);
    }

    private ContextTarget Target(string elementId, ToolOrigin origin, string registration) =>
        ContextTarget.ForElement(_body, elementId, ProjectRoot.From(_root), origin: origin) with { RegistrationPath = registration };

    private string Registration(string name, string origin, string resource, string layout)
    {
        var registration = IoPath.Combine(_root, name);
        File.WriteAllText(registration, $"{origin}\r\nbody: {Fixture}\r\n{resource}{layout}");
        return registration;
    }

    /// <summary>A session's baseline elements, none when its reading is empty, which sends no delta at all.</summary>
    private static async Task<List<DiagramElement>> ElementsAsync(IDiagramSession session) =>
        [.. (await session.BaselineAsync(TestContext.Current.CancellationToken)).OfType<DiagramAddDelta>().SelectMany(add => add.Elements)];

    private static async Task<byte[][]> BytesAsync(params string[] paths) =>
        await Task.WhenAll(paths.Select(path => File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));

    private static (double X, double Y) Position(IReadOnlyList<DiagramElement> elements, string id) =>
        Assert.Single(elements, element => element.Id == id) is var element ? (element.X, element.Y) : default;

    private async Task<DatabricksModel> ReadAsync() =>
        DatabricksModel.Read(LineDocument.Parse(await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken)));
}
