using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// What the commands write through the binding: a value reads back as the same text (databricks-disl-fbl finding B9),
/// a key is found among its entry's own keys only (B10), and a list written on one line is edited where it is written
/// (B12, which the writers refused before task 12) - Requirement 6.4.
/// </summary>
public sealed class DatabricksWrittenValuesTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("databricks-written-values");
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly DatabricksContextPropertyProvider _properties;

    public DatabricksWrittenValuesTests()
    {
        _root = _testFolder.Path;
        _provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        _properties = new DatabricksContextPropertyProvider(
            _provider.GetRequiredService<IHistoryStackStore>(),
            _provider.GetRequiredService<IDatabricksDocumentStore>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _testFolder.Dispose();
    }

    // B9: a written value reads back as the same text.

    [Fact]
    public async Task ARunIfOfAnUnclosedFlowSequence_IsWritten_AndReadsBackAsTheSameText()
    {
        // Arrange.
        var body = IoPath.Combine(_root, "raw-values.yml");
        File.Copy(Fixture("raw-values.yml"), body);

        // Act.
        var result = await _properties.SetAsync(
            ContextTarget.ForElement(body, "task:plain", ProjectRoot.From(_root)),
            DatabricksContextPropertyProvider.RunIfProperty,
            "[x",
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        Assert.Equal("[x", DatabricksModel.Read(LineDocument.Parse(text)).Jobs.SelectMany(job => job.Tasks).Single(task => task.Key == "plain").RunIf);
    }

    [Theory]
    [InlineData("[x")]
    [InlineData("yes")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("*nothing")]
    [InlineData("&anchor")]
    [InlineData("!tag")]
    [InlineData("%percent")]
    [InlineData("@at")]
    public async Task ARunIf_IsWrittenSoThatItReadsBackAsTheSameText(string value)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("raw-values.yml");

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksRunIfCommand(body, "", "plain", value));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        AssertReadsBackAsText(await editing.TextAsync(), value, "run_if");
        Assert.Equal(value, (await editing.TaskAsync("plain")).RunIf);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("yes")]
    [InlineData("null")]
    [InlineData("@at")]
    public async Task ARenamedTaskKey_AndEveryReferenceToIt_ReadBackAsTheSameText(string value)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("raw-values.yml");

        // Act.
        var result = await editing.RunAsync(body => new RenameDatabricksTaskCommand(body, "", "plain", value));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        AssertReadsBackAsText(await editing.TextAsync(), value, "task_key");
        Assert.Equal(value, Assert.Single((await editing.TaskAsync("follower")).DependsOn).TaskKey);
        Assert.Equal(value, (await editing.JobAsync()).Tasks[0].Key);
    }

    [Fact]
    public async Task AClusterKey_IsWrittenSoThatItReadsBackAsTheSameText()
    {
        // Arrange.
        using var editing = DatabricksEditing.Holding(
            "databricks.yml",
            "resources:\n  jobs:\n    j:\n      job_clusters:\n        - job_cluster_key: \"true\"\n      tasks:\n        - task_key: t\n          notebook_task:\n            notebook_path: n\n");

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksClusterCommand(body, "", "t", "true"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        AssertReadsBackAsText(await editing.TextAsync(), "true", "job_cluster_key");
        Assert.Equal("true", (await editing.TaskAsync("t")).ClusterKey);
    }

    [Fact]
    public async Task ANewJobResource_HasItsNameWrittenSoThatItReadsBackAsTheSameText()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("raw-values.yml");

        // Act.
        var result = await editing.RunAsync(body => new AddDatabricksResourceCommand(body, "jobs", "yes"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        AssertReadsBackAsText(await editing.TextAsync(), "yes", "name");
    }

    [Theory]
    [InlineData("notebook", "notebook_path", "yes")]
    [InlineData("notebook", "notebook_path", "123")]
    [InlineData("notebook", "notebook_path", "notebooks/a: b")]
    [InlineData("python", "python_file", "*nothing")]
    [InlineData("sql", "query_id", "null")]
    public async Task ANewTasksSource_IsWrittenSoThatItReadsBackAsTheSameText(string type, string key, string value)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("raw-values.yml");

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksTaskCommand(body, "", "added", type, value));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        AssertReadsBackAsText(await editing.TextAsync(), value, key);
        Assert.Equal(value, (await editing.TaskAsync("added")).Source);
    }

    // B10: a key is found among its entry's own keys only.

    [Fact]
    public async Task RenamingATaskWhoseNestedTaskComesFirst_RenamesItsOwnKey_NotTheNestedOne()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("for-each-before-key.yml");

        // Act.
        var result = await editing.RunAsync(body => new RenameDatabricksTaskCommand(body, "", "outer", "renamed"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("              task_key: inner", await editing.TextAsync(), StringComparison.Ordinal);
        Assert.Equal(["renamed", "mapping_first"], (await editing.JobAsync()).Tasks.Select(task => task.Key));
        Assert.Equal("renamed", Assert.Single((await editing.TaskAsync("mapping_first")).DependsOn).TaskKey);
    }

    [Fact]
    public async Task ARunIfAddedToATaskWhoseFirstKeyIsAMapping_IsTheTasksOwnKey()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("for-each-before-key.yml");

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksRunIfCommand(body, "", "mapping_first", "ALL_DONE"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var task = await editing.TaskAsync("mapping_first");
        Assert.Equal("ALL_DONE", task.RunIf);
        Assert.Equal("notebooks/mapping_first", task.Source);
    }

    [Fact]
    public async Task ADependencyAddedToATaskWhoseFirstKeyIsAMapping_IsTheTasksOwnKey()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("for-each-before-key.yml");

        // Act.
        var result = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", "mapping_first", "outer"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var outer = await editing.TaskAsync("outer");
        Assert.Equal("mapping_first", Assert.Single(outer.DependsOn).TaskKey);
        Assert.Equal("for-each", outer.Type);
    }

    // B12: a list written on one line is edited where it is written, as FBL writes a flow collection (FBL 0.10
    // sections 4.3 and 6.2), where the writers refused the edit; one undo gives the bytes back.

    [Fact]
    public async Task ATaskAddedToTasksWrittenAsAnEmptyFlowSequence_IsWritten_AndOneUndoGivesTheBytesBack()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("flow-collections.yml");
        var before = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksTaskCommand(body, "empty_flow", "added", "notebook", "notebooks/added"));
        var model = await editing.ModelAsync();
        var undone = await editing.UndoAsync(result);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var added = Assert.Single(model.Jobs.Single(job => job.Key == "empty_flow").Tasks);
        Assert.Equal(("added", "notebook", "notebooks/added"), (added.Key, added.Type, added.Source));
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(before, await editing.BytesAsync());
    }

    [Fact]
    public async Task ADependencyAddedToDependsOnWrittenOnOneLine_IsWritten()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("flow-dependencies.yml");

        // Act.
        var result = await editing.RunAsync(body => new ConnectDatabricksTasksCommand(body, "", "c", "b"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["a", "c"], (await editing.TaskAsync("b")).DependsOn.Select(dependency => dependency.TaskKey));
    }

    [Fact]
    public async Task ADependencyRemovedFromDependsOnWrittenOnOneLine_IsRemoved()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("flow-dependencies.yml");

        // Act.
        var result = await editing.RunAsync(body => new DisconnectDatabricksTasksCommand(body, "", "a", "b"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty((await editing.TaskAsync("b")).DependsOn);
    }

    [Fact]
    public async Task ALibraryRemovedFromAJsonArrayWrittenOnOneLine_IsRemoved()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("settings-one-line.json");

        // Act.
        var result = await editing.RunAsync(body => new RemoveDatabricksLibraryCommand(body, "", "file", "transformations/b.py"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([("notebook", "transformations/a")], (await editing.PipelineAsync()).Libraries.Select(library => (library.Kind, library.Path)));
    }

    [Fact]
    public async Task ALibraryAddedToAYamlListWrittenOnOneLine_IsWritten()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("flow-collections.yml");

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksLibraryCommand(body, "flow_pipeline", "notebook", "transformations/three"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            ["transformations/one", "transformations/two.py", "transformations/three"],
            (await editing.PipelineAsync()).Libraries.Select(library => library.Path));
    }

    private static string Fixture(string name) => DatabricksEditing.Fixture(name);

    /// <summary>
    /// That <paramref name="value"/> is the text of a <paramref name="key"/> in the written document, and is
    /// written plain nowhere: a plain scalar is typed by the YAML core schema, so <c>123</c> would read back
    /// as a number and <c>*nothing</c> as an alias.
    /// </summary>
    private static void AssertReadsBackAsText(string text, string value, string key)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        var found = new List<YamlScalarNode>();
        Collect(stream.Documents[0].RootNode, key, found);
        var written = found.Where(node => node.Value == value).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, node => Assert.NotEqual(ScalarStyle.Plain, node.Style));
    }

    /// <summary>Every scalar value of a <paramref name="key"/> under <paramref name="node"/>, into <paramref name="found"/>.</summary>
    private static void Collect(YamlNode node, string key, List<YamlScalarNode> found)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach ((YamlNode name, YamlNode child) in mapping.Children)
                {
                    if (name is YamlScalarNode { Value: { } text } && text == key && child is YamlScalarNode scalar)
                    {
                        found.Add(scalar);
                    }

                    Collect(child, key, found);
                }

                break;
            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                {
                    Collect(child, key, found);
                }

                break;
        }
    }
}
