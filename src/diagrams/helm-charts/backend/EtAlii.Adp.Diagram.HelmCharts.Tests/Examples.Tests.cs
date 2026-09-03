using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The shipped examples are the type's first fixtures, and a red example is a broken example
/// (Requirement 11.6): every one of them judges clean, every one of them is a chart at all,
/// and the interesting states they were vendored to demonstrate actually hold.
/// </summary>
public class ExamplesTests
{
    public static TheoryData<string> ExampleNames => new("hello-world", "prometheus", "nginx");

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void EveryShippedExample_JudgesClean(string name)
    {
        // Arrange.
        var chart = new HelmChartReader().Read(Example(name));

        // Act.
        var problems = HelmRuleSet.Judge(chart);

        // Assert.
        Assert.True(chart.IsChart, $"{name} is not a chart at all.");
        Assert.True(
            problems.Count == 0,
            $"{name} judges red: {string.Join("; ", problems.Select(problem => problem.Message))}");
    }

    [Fact]
    public void Prometheus_DemonstratesUnvendoredConditionalDependencies()
    {
        // Arrange & act.
        var chart = new HelmChartReader().Read(Example("prometheus"));
        var graph = HelmGraph.Derive(chart);

        // Assert.
        // Four conditional dependencies, none vendored - all open ends, none a finding.
        Assert.Equal(4, chart.Dependencies.Count);
        Assert.All(chart.Dependencies, dependency => Assert.Equal(ConditionState.On, dependency.State));
        Assert.All(graph.Resolution.Dependencies, resolved => Assert.False(resolved.IsResolved));
    }

    [Fact]
    public void Nginx_DemonstratesTheResolvedLibrarySubchart_AndTheAuthoredLayering()
    {
        // Arrange & act.
        var chart = new HelmChartReader().Read(Example("nginx"));
        var graph = HelmGraph.Derive(chart);

        // Assert.
        // The common library chart is vendored at the exact version the lock pins.
        var resolved = Assert.Single(graph.Resolution.Dependencies);
        Assert.True(resolved.IsResolved);
        Assert.Equal("common", resolved.Vendored!.EntryName);
        Assert.Equal("library", resolved.Vendored.ChartType);
        Assert.Equal(
            chart.Lock!.Entries.Single(entry => entry.Name == "common").Version,
            resolved.Vendored.ChartVersion);

        // And the ADP-authored override stack is present and recognized.
        Assert.Equal(3, chart.Values.Count);
        Assert.Contains(chart.Values, values => values.RelativePath == "values-dev.yaml" && !values.IsDefault);
        Assert.Contains(chart.Values, values => values.RelativePath == "values-prod.yaml" && !values.IsDefault);
    }

    /// <summary>The module's own examples folder, found by walking up from the test binary.</summary>
    private static string Example(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "helm-charts", "examples", name);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"The example '{name}' was not found above the test binary.");
    }
}
