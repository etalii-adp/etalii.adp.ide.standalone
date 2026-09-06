using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The databricks.yml reading: structure, line ranges, and unmodelled constructs landing as
/// UnknownNodes rather than vanishing (databricks-diagrams Requirements 2.4 and 3).
/// </summary>
public class BundleParserTests
{
    private static BundleModel Parse(string name)
    {
        var document = DatabricksDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name)));
        return BundleParser.Parse(DatabricksYaml.Root(document), document);
    }

    [Fact]
    public void TheBundleFixture_ReadsItsNameIncludesAndVariables()
    {
        // Arrange & act.
        var bundle = Parse("bundle.yml");

        // Assert.
        Assert.Equal("lakehouse-nightly", bundle.Name);
        var include = Assert.Single(bundle.Includes);
        Assert.Equal("resources/*.yml", include.Glob);
        Assert.Equal(2, bundle.Variables.Count);
        Assert.Equal("catalog", bundle.Variables[0].Name);
        Assert.Equal("lakehouse_dev", bundle.Variables[0].Default);
        Assert.Equal("The Unity Catalog written to.", bundle.Variables[0].Description);
        Assert.Equal("warehouse_id", bundle.Variables[1].Name);
        Assert.Equal("4b9b953939869799", bundle.Variables[1].Default);
    }

    [Fact]
    public void TheBundleFixture_ReadsItsModelledResources_WithTheirLines()
    {
        // Arrange & act.
        var bundle = Parse("bundle.yml");

        // Assert.
        Assert.Equal(2, bundle.Resources.Count);
        var job = bundle.Resources[0];
        Assert.Equal("jobs", job.Kind);
        Assert.Equal("nightly_ingest", job.Key);
        // The key and its body: `nightly_ingest:` on line 20, `name:` on 21 (zero-based).
        Assert.Equal(new LineRange(20, 21), job.Lines);
        Assert.Equal(("pipelines", "bronze_to_gold"), (bundle.Resources[1].Kind, bundle.Resources[1].Key));
    }

    [Fact]
    public void UnmodelledConstructs_LandAsUnknownNodes_NotNowhere()
    {
        // Arrange & act.
        var bundle = Parse("bundle.yml");

        // Assert.
        // The `sync:` root key and the `experiments` resource kind are real configuration this
        // module does not model; they draw generically and are never written.
        Assert.Contains(bundle.UnknownNodes, node => node.Path == "sync");
        Assert.Contains(bundle.UnknownNodes, node => node.Path == "resources.experiments");
        Assert.Equal(2, bundle.UnknownNodes.Count);
    }

    [Fact]
    public void Targets_CarryModeDefaultAndOverrides()
    {
        // Arrange & act.
        var bundle = Parse("bundle.yml");

        // Assert.
        Assert.Equal(2, bundle.Targets.Count);
        var dev = bundle.Targets[0];
        Assert.Equal(("dev", "development", true), (dev.Name, dev.Mode, dev.IsDefault));
        Assert.Empty(dev.Overrides);
        var prod = bundle.Targets[1];
        Assert.Equal(("prod", "production", false), (prod.Name, prod.Mode, prod.IsDefault));
        var overridden = Assert.Single(prod.Overrides);
        Assert.Equal(("jobs", "nightly_ingest"), (overridden.Kind, overridden.Key));
    }

    [Fact]
    public void AFileThatIsNoBundle_ReadsAsTheEmptyBundle()
    {
        // Arrange & act.
        var pipeline = Parse("pipeline.json");

        // Assert.
        // A pipeline settings file has no bundle:, resources: or targets: - the bundle reading
        // of it is empty rather than an error.
        Assert.Equal("", pipeline.Name);
        Assert.Empty(pipeline.Resources);
        Assert.Empty(pipeline.Targets);
    }
}
