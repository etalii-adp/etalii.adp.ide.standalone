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
/// What the three writers write: a value reads back as the same text (databricks-disl-fbl finding B9),
/// a key is found among its entry's own keys only (B10), and an edit into a list written on one line is
/// refused before any splice (B12) - Requirement 6.4.
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
        var document = LineDocument.Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
        Assert.Equal("[x", TaskOf(document, "plain").RunIf);
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
    public void ARunIf_IsWrittenSoThatItReadsBackAsTheSameText(string value)
    {
        // Arrange.
        var document = Load("raw-values.yml");

        // Act.
        var refusal = JobWriter.SetRunIf(document, Job(document), "plain", value);

        // Assert.
        Assert.Equal("", refusal);
        AssertReadsBackAsText(document, value, "run_if");
        Assert.Equal(value, TaskOf(document, "plain").RunIf);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("yes")]
    [InlineData("null")]
    [InlineData("@at")]
    public void ARenamedTaskKey_AndEveryReferenceToIt_ReadBackAsTheSameText(string value)
    {
        // Arrange.
        var document = Load("raw-values.yml");

        // Act.
        var refusal = JobWriter.RenameTask(document, Job(document), "plain", value);

        // Assert.
        Assert.Equal("", refusal);
        AssertReadsBackAsText(document, value, "task_key");
        Assert.Equal(value, Assert.Single(TaskOf(document, "follower").DependsOn).TaskKey);
        Assert.Equal(value, Job(document).Tasks[0].Key);
    }

    [Fact]
    public void AClusterKey_IsWrittenSoThatItReadsBackAsTheSameText()
    {
        // Arrange.
        var document = LineDocument.Parse(
            "resources:\n  jobs:\n    j:\n      job_clusters:\n        - job_cluster_key: \"true\"\n      tasks:\n        - task_key: t\n          notebook_task:\n            notebook_path: n\n");

        // Act.
        var refusal = JobWriter.SetCluster(document, Job(document), "t", "true");

        // Assert.
        Assert.Equal("", refusal);
        AssertReadsBackAsText(document, "true", "job_cluster_key");
        Assert.Equal("true", TaskOf(document, "t").ClusterKey);
    }

    [Fact]
    public void ANewJobResource_HasItsNameWrittenSoThatItReadsBackAsTheSameText()
    {
        // Arrange.
        var document = Load("raw-values.yml");

        // Act.
        var refusal = BundleWriter.AddResourceSkeleton(document, DatabricksModel.Read(document).Bundle, "jobs", "yes");

        // Assert.
        Assert.Equal("", refusal);
        AssertReadsBackAsText(document, "yes", "name");
    }

    // B10: a key is found among its entry's own keys only.

    [Fact]
    public void RenamingATaskWhoseNestedTaskComesFirst_RenamesItsOwnKey_NotTheNestedOne()
    {
        // Arrange.
        var document = Load("for-each-before-key.yml");

        // Act.
        var refusal = JobWriter.RenameTask(document, Job(document), "outer", "renamed");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Contains("              task_key: inner", document.Text, StringComparison.Ordinal);
        Assert.Equal(["renamed", "mapping_first"], Job(document).Tasks.Select(task => task.Key));
        Assert.Equal("renamed", Assert.Single(TaskOf(document, "mapping_first").DependsOn).TaskKey);
    }

    [Fact]
    public void ARunIfAddedToATaskWhoseFirstKeyIsAMapping_IsTheTasksOwnKey()
    {
        // Arrange.
        var document = Load("for-each-before-key.yml");

        // Act.
        var refusal = JobWriter.SetRunIf(document, Job(document), "mapping_first", "ALL_DONE");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal("ALL_DONE", TaskOf(document, "mapping_first").RunIf);
        Assert.Equal("notebooks/mapping_first", TaskOf(document, "mapping_first").Source);
    }

    [Fact]
    public void ADependencyAddedToATaskWhoseFirstKeyIsAMapping_IsTheTasksOwnKey()
    {
        // Arrange.
        var document = Load("for-each-before-key.yml");

        // Act.
        var refusal = JobWriter.Connect(document, Job(document), "mapping_first", "outer");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal("mapping_first", Assert.Single(TaskOf(document, "outer").DependsOn).TaskKey);
        Assert.Equal("for-each", TaskOf(document, "outer").Type);
    }

    // B12: an edit into a list written on one line is refused before any splice.

    [Fact]
    public void ATaskAddedToTasksWrittenAsAnEmptyFlowSequence_IsRefused_AndNothingIsWritten()
    {
        // Arrange.
        var document = Load("flow-collections.yml");
        var before = document.Text;
        var job = DatabricksModel.Read(document).Jobs.Single(candidate => candidate.Key == "empty_flow");

        // Act.
        var refusal = JobWriter.InsertTask(document, job, "added", "notebook", "notebooks/added");

        // Assert.
        Assert.Equal(DatabricksSplices.OneLineListRefusal, refusal);
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void ADependencyAddedToDependsOnWrittenOnOneLine_IsRefused_AndNothingIsWritten()
    {
        // Arrange.
        var document = Load("flow-dependencies.yml");
        var before = document.Text;

        // Act.
        var refusal = JobWriter.Connect(document, Job(document), "c", "b");

        // Assert.
        Assert.Equal(DatabricksSplices.OneLineListRefusal, refusal);
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void ADependencyRemovedFromDependsOnWrittenOnOneLine_IsRefused_AndNothingIsWritten()
    {
        // Arrange.
        var document = Load("flow-dependencies.yml");
        var before = document.Text;

        // Act.
        var refusal = JobWriter.Disconnect(document, Job(document), "a", "b");

        // Assert.
        Assert.Equal(DatabricksSplices.OneLineListRefusal, refusal);
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void ALibraryRemovedFromAJsonArrayWrittenOnOneLine_IsRefused_AndNothingIsWritten()
    {
        // Arrange.
        var document = Load("settings-one-line.json");
        var before = document.Text;
        var pipeline = Assert.Single(DatabricksModel.Read(document).Pipelines);

        // Act.
        var refusal = PipelineWriter.RemoveLibrary(document, pipeline, "file", "transformations/b.py");

        // Assert.
        Assert.Equal(DatabricksSplices.OneLineListRefusal, refusal);
        Assert.Equal(before, document.Text);
    }

    [Fact]
    public void ALibraryAddedToAYamlListWrittenOnOneLine_IsRefused_AndNothingIsWritten()
    {
        // Arrange.
        var document = Load("flow-collections.yml");
        var before = document.Text;
        var pipeline = Assert.Single(DatabricksModel.Read(document).Pipelines);

        // Act.
        var refusal = PipelineWriter.InsertLibrary(document, pipeline, "notebook", "transformations/three");

        // Assert.
        Assert.Equal(DatabricksSplices.OneLineListRefusal, refusal);
        Assert.Equal(before, document.Text);
    }

    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static LineDocument Load(string name) => LineDocument.Parse(File.ReadAllText(Fixture(name)));

    private static JobModel Job(LineDocument document) =>
        Assert.Single(DatabricksModel.Read(document).Jobs);

    private static JobTask TaskOf(LineDocument document, string key) =>
        DatabricksModel.Read(document).Jobs.SelectMany(job => job.Tasks).Single(task => task.Key == key);

    /// <summary>
    /// That <paramref name="value"/> is the text of a <paramref name="key"/> in the written document, and is
    /// written plain nowhere: a plain scalar is typed by the YAML core schema, so <c>123</c> would read back
    /// as a number and <c>*nothing</c> as an alias.
    /// </summary>
    private static void AssertReadsBackAsText(LineDocument document, string value, string key)
    {
        var written = Scalars(document, key).Where(node => node.Value == value).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, node => Assert.NotEqual(ScalarStyle.Plain, node.Style));
    }

    /// <summary>Every scalar value of a <paramref name="key"/> anywhere in the document.</summary>
    private static List<YamlScalarNode> Scalars(LineDocument document, string key)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(document.Text));
        var found = new List<YamlScalarNode>();
        Visit(stream.Documents[0].RootNode);
        return found;

        void Visit(YamlNode node)
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

                        Visit(child);
                    }

                    break;
                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        Visit(child);
                    }

                    break;
            }
        }
    }
}
