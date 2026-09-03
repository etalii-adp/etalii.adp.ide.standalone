using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The bundle splices: resource skeletons land beside their siblings, scalars rewrite one line,
/// and invalid writes are refused before any splice (databricks-diagrams Requirements 2.2 and 6.4).
/// </summary>
public class BundleWriterTests
{
    private static DatabricksDocument Load() =>
        DatabricksDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "bundle.yml")));

    private static BundleModel Bundle(DatabricksDocument document) =>
        BundleParser.Parse(DatabricksYaml.Root(document), document);

    [Fact]
    public void AddResourceSkeleton_LandsAfterItsKindsLastSibling()
    {
        // Arrange.
        var document = Load();

        // Act.
        var refusal = BundleWriter.AddResourceSkeleton(document, Bundle(document), "jobs", "weekly_rollup");

        // Assert.
        Assert.Equal("", refusal);
        var reparsed = Bundle(document);
        Assert.Equal(3, reparsed.Resources.Count);
        // The new job sits with the jobs, not at the end of the file.
        Assert.Equal(
            new[] { ("jobs", "nightly_ingest"), ("jobs", "weekly_rollup"), ("pipelines", "bronze_to_gold") },
            reparsed.Resources.Select(resource => (resource.Kind, resource.Key)));
        // The skeleton is complete enough to open: the job arrives with a starter task.
        var job = JobParser.Parse(DatabricksYaml.Root(document), document)
            .Single(candidate => candidate.Key == "weekly_rollup");
        Assert.Single(job.Tasks);
    }

    [Fact]
    public void AddResourceSkeleton_OnAFileWithoutResources_CreatesTheSection()
    {
        // Arrange.
        var document = DatabricksDocument.Parse("bundle:\r\n  name: fresh\r\n");

        // Act.
        var refusal = BundleWriter.AddResourceSkeleton(document, Bundle(document), "pipelines", "first");

        // Assert.
        Assert.Equal("", refusal);
        var pipeline = Assert.Single(PipelineParser.Parse(DatabricksYaml.Root(document), document));
        Assert.Equal("first", pipeline.Key);
        Assert.Single(pipeline.Libraries);
    }

    [Fact]
    public void AddResourceSkeleton_Refusals_HappenBeforeAnySplice()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("already there", BundleWriter.AddResourceSkeleton(document, Bundle(document), "jobs", "nightly_ingest"), StringComparison.Ordinal);
        Assert.Contains("no 'experiments' resource kind", BundleWriter.AddResourceSkeleton(document, Bundle(document), "experiments", "fresh"), StringComparison.Ordinal);
        Assert.Contains("needs a key", BundleWriter.AddResourceSkeleton(document, Bundle(document), "jobs", ""), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void SetName_RewritesTheOneLine()
    {
        // Arrange.
        var document = Load();
        var before = document.Lines.Count;

        // Act.
        var refusal = BundleWriter.SetName(document, Bundle(document), "lakehouse-hourly");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(before, document.Lines.Count);
        Assert.Equal("lakehouse-hourly", Bundle(document).Name);
    }

    [Fact]
    public void SetName_ToNothing_IsRefused()
    {
        // Arrange.
        var document = Load();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("needs a name", BundleWriter.SetName(document, Bundle(document), ""), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }
}
