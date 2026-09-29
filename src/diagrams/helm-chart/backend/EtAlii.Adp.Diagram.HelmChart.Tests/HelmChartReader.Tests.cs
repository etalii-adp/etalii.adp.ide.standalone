using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The reader over the three fixture charts: the full inventory from <c>well-formed/</c>,
/// graceful degradation from <c>broken/</c>, and the legacy/library/sealed corners from
/// <c>unconventional/</c>. Assertions go list by list and field by field - never record
/// equality over a record holding lists.
/// </summary>
public class HelmChartReaderTests
{
    private static string Fixture(string name) =>
        IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void TheWellFormedChart_ReadsItsMetadata()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        Assert.True(chart.IsChart);
        Assert.NotNull(chart.Metadata);
        Assert.Equal("shop", chart.Metadata.Name);
        Assert.Equal("1.4.0", chart.Metadata.Version);
        Assert.Equal("7.2", chart.Metadata.AppVersion);
        Assert.Equal("application", chart.Metadata.ChartType);
        Assert.False(chart.Legacy);
        Assert.Null(chart.MetadataFailure);
    }

    [Fact]
    public void TheValuesStack_LeadsWithTheDefaultLayer()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        Assert.Equal(2, chart.Values.Count);
        Assert.Equal("values.yaml", chart.Values[0].RelativePath);
        Assert.True(chart.Values[0].IsDefault);
        Assert.True(chart.Values[0].HasGlobal);
        Assert.Contains("cache", chart.Values[0].TopLevelKeys);
        Assert.Equal("values-staging.yaml", chart.Values[1].RelativePath);
        Assert.False(chart.Values[1].IsDefault);
        Assert.NotNull(chart.Schema);
    }

    [Fact]
    public void Templates_GetTheirRolesAndTheirFacts()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        var byPath = chart.Templates.ToDictionary(template => template.RelativePath);
        Assert.Equal(TemplateRole.Manifest, byPath["templates/deployment.yaml"].Role);
        Assert.Equal(["Deployment"], byPath["templates/deployment.yaml"].Facts.Kinds);
        Assert.Equal(["shop.fullname", "shop.labels"], byPath["templates/deployment.yaml"].Facts.References);
        Assert.Equal(["Ingress", "Service"], byPath["templates/service.yaml"].Facts.Kinds);
        Assert.Equal(TemplateRole.Partial, byPath["templates/_helpers.tpl"].Role);
        Assert.Equal(["shop.fullname", "shop.labels"], byPath["templates/_helpers.tpl"].Facts.Defines);
        Assert.Equal(TemplateRole.Notes, byPath["templates/NOTES.txt"].Role);
        Assert.Equal(TemplateRole.Test, byPath["templates/tests/test-connection.yaml"].Role);
    }

    [Fact]
    public void Dependencies_CarryAliasConditionAndItsResolvedState()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        Assert.Equal(2, chart.Dependencies.Count);
        // Ordinally by effective name: "cache" (the alias) sorts before "postgres".
        var cache = chart.Dependencies[0];
        Assert.Equal("redis", cache.Name);
        Assert.Equal("cache", cache.Alias);
        Assert.Equal("cache", cache.EffectiveName);
        Assert.Equal(ConditionState.On, cache.State);
        Assert.True(cache.Line > 0);
        var postgres = chart.Dependencies[1];
        Assert.Equal("postgres", postgres.EffectiveName);
        Assert.Equal(ConditionState.None, postgres.State);
    }

    [Fact]
    public void VendoredContent_IsReadOneLevelDeep()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        var redis = Assert.Single(chart.Vendored);
        Assert.Equal("redis", redis.EntryName);
        Assert.False(redis.Sealed);
        Assert.Equal("redis", redis.ChartName);
        Assert.Equal("17.3.2", redis.ChartVersion);
        Assert.Equal(1, redis.TemplateCount);
        Assert.Equal(0, redis.DeeperCount);
    }

    [Fact]
    public void TheLockAndTheCrds_AreSummarized()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("well-formed"));

        // Assert.
        Assert.NotNull(chart.Lock);
        Assert.Equal(["postgres", "redis"], chart.Lock.Entries.Select(entry => entry.Name).ToArray());
        Assert.Equal("17.3.2", chart.Lock.Entries[1].Version);
        Assert.NotNull(chart.Crds);
        Assert.Equal(1, chart.Crds.FileCount);
        Assert.Empty(chart.Crds.Failures);
    }

    [Fact]
    public void ABrokenValuesFile_CostsOnlyItself()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("broken"));

        // Assert.
        // Chart.yaml parsed (name only), the values did not - and the rest still read.
        Assert.True(chart.IsChart);
        Assert.NotNull(chart.Metadata);
        Assert.Equal("fractured", chart.Metadata.Name);
        Assert.Null(chart.Metadata.Version);
        var values = Assert.Single(chart.Values);
        Assert.NotNull(values.Failure);
        Assert.True(values.Failure.Line > 0);
        // The templated kind is unknown by design; the literal apiVersion still counts.
        var template = Assert.Single(chart.Templates);
        Assert.Empty(template.Facts.Kinds);
        Assert.Equal(["v1"], template.Facts.ApiVersions);
    }

    [Fact]
    public void TheLegacyChart_ReadsRequirementsAndItsLock()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("unconventional"));

        // Assert.
        Assert.True(chart.Legacy);
        Assert.NotNull(chart.Metadata);
        Assert.True(chart.Metadata.IsLibrary);
        Assert.Equal("not.semver.at-all", chart.Metadata.Version);
        var dependency = Assert.Single(chart.Dependencies);
        Assert.Equal("deep", dependency.Name);
        Assert.NotNull(chart.Lock);
        Assert.Equal("requirements.lock", chart.Lock.RelativePath);
        Assert.Equal("1.0.0", Assert.Single(chart.Lock.Entries).Version);
    }

    [Fact]
    public void ASealedArchive_IsNamedButNeverUnpacked()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("unconventional"));

        // Assert.
        var sealedEntry = Assert.Single(chart.Vendored, entry => entry.Sealed);
        Assert.Equal("sealed", sealedEntry.EntryName);
        Assert.Equal("charts/sealed-9.9.9.tgz", sealedEntry.RelativePath);
        Assert.Null(sealedEntry.ChartName);
    }

    [Fact]
    public void DeeperVendoredNesting_IsACountNotARecursion()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(Fixture("unconventional"));

        // Assert.
        var deep = Assert.Single(chart.Vendored, entry => !entry.Sealed);
        Assert.Equal("deep", deep.EntryName);
        Assert.Equal(1, deep.TemplateCount);
        Assert.Equal(1, deep.DeeperCount);
    }

    [Fact]
    public void AFolderWithNoChartYaml_IsNotAChart()
    {
        // Arrange.
        var scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(IoPath.Combine(scratch, "unrelated.txt"), "not a chart");
        try
        {
            // Act.
            var chart = new HelmChartReader().Read(scratch);

            // Assert.
            Assert.False(chart.IsChart);
            Assert.Empty(chart.Templates);
        }
        finally
        {
            TestFolder.TryDelete(scratch);
        }
    }

    [Fact]
    public void ADeletedFolder_DegradesTheSameWay()
    {
        // Arrange & Act.
        var chart = new HelmChartReader().Read(
            IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N")));

        // Assert.
        Assert.False(chart.IsChart);
    }

    [Fact]
    public void ArchiveNames_LoseTheirVersionTailForMatching()
    {
        // Assert.
        // The matcher compares entry names, so the tail has to go - but only a tail that
        // looks like a version, so a dash inside a real name survives.
        Assert.Equal("common", HelmChartReader.StripVersionTail("common-2.31.4.tgz"));
        Assert.Equal("kube-state-metrics", HelmChartReader.StripVersionTail("kube-state-metrics-8.4.0.tgz"));
        Assert.Equal("no-version", HelmChartReader.StripVersionTail("no-version.tgz"));
    }
}
