using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// Menus, property rows, drops and commands act on the resource the registration names, not on the
/// file's first (databricks-disl-fbl finding B3, Requirement 5.3): in a file with two jobs and two
/// pipelines, two registrations that name different resources each show and edit their own.
/// </summary>
public sealed class DatabricksRegistrationResourceTests : IDisposable
{
    private const string Fixture = "two-jobs-two-pipelines.yml";

    private readonly TestFolder _testFolder = TestFolder.Create("databricks-registration-resource");
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextPropertyProvider _properties;
    private readonly DatabricksContextActionProvider _actions;
    private readonly string _body;

    public DatabricksRegistrationResourceTests()
    {
        _root = _testFolder.Path;
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        var history = _provider.GetRequiredService<IHistoryStackStore>();
        var documents = _provider.GetRequiredService<IDatabricksDocumentStore>();
        _properties = new DatabricksContextPropertyProvider(history, documents);
        _actions = new DatabricksContextActionProvider(history, documents);
        _body = IoPath.Combine(_root, Fixture);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", Fixture), _body);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task TwoPipelineRegistrations_EachShowTheirOwnPipelinesRows()
    {
        // Arrange.
        var first = Target("pipeline", ServiceCollectionAddDatabricksExtension.PipelineOrigin, Registration("databricks/pipeline", "pipe_a"));
        var second = Target("pipeline", ServiceCollectionAddDatabricksExtension.PipelineOrigin, Registration("databricks/pipeline", "pipe_b"));

        // Act.
        var firstRows = await _properties.DescribeAsync(first, TestContext.Current.CancellationToken);
        var secondRows = await _properties.DescribeAsync(second, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Pipe A", Value(firstRows, $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}name"));
        Assert.Equal("Pipe B", Value(secondRows, $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}name"));
        Assert.Equal("silver", Value(secondRows, $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}schema"));
    }

    [Fact]
    public async Task ThePropertyRowOfTheSecondPipeline_EditsTheSecondPipeline()
    {
        // Arrange.
        var target = Target("pipeline", ServiceCollectionAddDatabricksExtension.PipelineOrigin, Registration("databricks/pipeline", "pipe_b"));

        // Act.
        var result = await _properties.SetAsync(
            target, $"{DatabricksContextPropertyProvider.PipelineScalarPrefix}name", "Renamed B", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var model = await ReadAsync();
        Assert.Equal(["Pipe A", "Renamed B"], model.Pipelines.Select(pipeline => pipeline.Name));
    }

    [Fact]
    public async Task ATaskKeyBothJobsShare_IsEditedInTheJobTheRegistrationNames()
    {
        // Arrange.
        var target = Target("task:ingest", ServiceCollectionAddDatabricksExtension.JobOrigin, Registration("databricks/job", "job_b"));

        // Act.
        var result = await _properties.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var model = await ReadAsync();
        Assert.Equal(["", "ALL_DONE"], model.Jobs.Select(job => job.Tasks.Single(task => task.Key == "ingest").RunIf));
    }

    [Fact]
    public async Task ATaskDroppedOnTheSecondJobsCanvas_LandsInTheSecondJob()
    {
        // Arrange.
        var target = Target(GestureIds.Placement(120, 80), ServiceCollectionAddDatabricksExtension.JobOrigin, Registration("databricks/job", "job_b"));

        // Act.
        var result = await _actions.ExecuteAsync(target, $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<ContextExecutionCompleted>(result);
        var model = await ReadAsync();
        Assert.Equal(2, model.Jobs[0].Tasks.Count);
        Assert.Equal(["ingest", "report", "notebook_1"], model.Jobs[1].Tasks.Select(task => task.Key));
    }

    [Fact]
    public async Task ALibraryAddedThroughTheSecondPipelinesMenu_LandsInTheSecondPipeline()
    {
        // Arrange.
        var target = Target("pipeline", ServiceCollectionAddDatabricksExtension.PipelineOrigin, Registration("databricks/pipeline", "pipe_b"));

        // Act.
        var result = await _actions.CommitAsync(
            target, $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", "transformations/b2", "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Completed, result.Error);
        var model = await ReadAsync();
        Assert.Single(model.Pipelines[0].Libraries);
        Assert.Equal(["transformations/b", "transformations/b2"], model.Pipelines[1].Libraries.Select(library => library.Path));
    }

    [Fact]
    public async Task ATargetResolvedThroughARegistration_CarriesThatRegistration()
    {
        // Arrange.
        var registration = Registration("databricks/job", "job_b");
        var resolver = new DatabricksContextSourceResolver(
            ServiceCollectionAddDatabricksExtension.JobOrigin,
            new DiagramFileRouter(new DatabricksOnlyCatalog()),
            new DatabricksDocumentStore(new FileWriter()),
            new DatabricksElementMapper());
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            [IoPath.GetFileName(registration)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration, IsContainer: false, SourceId: default, ProjectRoot.From(_root), ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        // Act.
        var resolution = await resolver.ResolveAsync(
            ShortGuid.NewShortGuid(),
            ProjectRoot.From(_root),
            new ContextSource { ElementId = new ElementId { Value = "task:report" } },
            [],
            parent,
            TestContext.Current.CancellationToken);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(registration, level.Target.RegistrationPath);
    }

    private ContextTarget Target(string elementId, ToolOrigin origin, string registration) =>
        ContextTarget.ForElement(_body, elementId, ProjectRoot.From(_root), origin: origin) with { RegistrationPath = registration };

    private string Registration(string mime, string resource)
    {
        var registration = IoPath.Combine(_root, $"{resource}.adp");
        File.WriteAllText(registration, $"{mime}\r\nbody: {Fixture}\r\nresource: {resource}\r\n");
        return registration;
    }

    private async Task<DatabricksModel> ReadAsync() =>
        DatabricksModel.Read(LineDocument.Parse(await File.ReadAllTextAsync(_body, TestContext.Current.CancellationToken)));

    private static string Value(IReadOnlyList<ContextPropertyDefinition> rows, string id) =>
        Assert.Single(rows, row => row.Id == id).Value;
}
