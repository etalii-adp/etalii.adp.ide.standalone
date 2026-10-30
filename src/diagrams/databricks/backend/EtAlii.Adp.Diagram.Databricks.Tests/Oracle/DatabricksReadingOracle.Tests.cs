using System.Text.Json;
using EtAlii.Adp.Diagram.Databricks.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Oracle;

/// <summary>
/// Every document of the parity corpus reads through the binding as the module's three readers read it before
/// databricks-disl-fbl task 11 (Requirement 4.3): the same bundle, jobs, tasks, dependencies, clusters, pipelines,
/// libraries and notifications, with the same values and the same lines, which the writers splice by. The readers are
/// kept beside this test as its oracle (<see cref="BundleParser"/>, <see cref="JobParser"/>, <see cref="PipelineParser"/>).
/// The one document that reads differently is finding B5's, by the reading adopted for it: FBL applies a merge key and
/// reads no entry through an alias, where YamlDotNet's tree did neither.
/// </summary>
public class DatabricksReadingOracleTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The fixture of finding B5: a merge key in a cluster, and a task list shared by an alias.</summary>
    private const string Anchors = "anchors-and-merge.yml";

    public static TheoryData<string> Documents() => [.. DatabricksParityCorpus.Documents().Select(row => row.Data).Where(name => name != Anchors)];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task TheBindingReadsWhatTheReadersRead(string name)
    {
        // Arrange.
        var document = LineDocument.Parse(await DatabricksParityCorpus.TextAsync(name, TestContext.Current.CancellationToken));
        var expected = Oracle(document);

        // Act.
        var actual = Read(document);

        // Assert.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task AMergeKeyIsApplied_AndNoEntryIsReadThroughAnAlias()
    {
        // Arrange.
        var document = LineDocument.Parse(await DatabricksParityCorpus.TextAsync(Anchors, TestContext.Current.CancellationToken));

        // Act.
        var model = DatabricksModel.Read(document);

        // Assert: the merged cluster has the anchored version and node type; the aliased job has no task of its own.
        var cluster = Assert.Single(model.Jobs.Single(job => job.Key == "anchored").Clusters);
        Assert.Equal(("15.4.x-scala2.12", "Standard_DS3_v2"), (cluster.SparkVersion, cluster.NodeType));
        Assert.Equal(2, model.Jobs.Single(job => job.Key == "anchored").Tasks.Count);
        Assert.Empty(model.Jobs.Single(job => job.Key == "aliased").Tasks);
    }

    private static string Oracle(LineDocument document)
    {
        try
        {
            var root = YamlAccess.Root(document);
            return JsonSerializer.Serialize(new DatabricksModel(BundleParser.Parse(root, document), JobParser.Parse(root, document), PipelineParser.Parse(root, document)), Options);
        }
        catch (YamlException exception)
        {
            return "unparseable: " + exception.Message;
        }
    }

    private static string Read(LineDocument document)
    {
        try
        {
            return JsonSerializer.Serialize(DatabricksModel.Read(document), Options);
        }
        catch (YamlException exception)
        {
            return "unparseable: " + exception.Message;
        }
    }
}
