using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The validator seam (Requirements 10.1, 10.8): the subject folder read, and every finding
/// rebased to project-relative - tested against a NESTED chart, because the chart-is-the-root
/// case is exactly where the rebasing bug hid in the sibling module.
/// </summary>
public class HelmValidatorTests : IDisposable
{
    private readonly string _root;

    public HelmValidatorTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IoPath.Combine(_root, "deploy", "my-chart"));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private DiagramValidationRequest Request(string folder) => new(
        Document: "helm/chart",
        BaseName: "helm-chart",
        RootPath: _root,
        BodyPath: IoPath.Combine(folder, "helm-chart.adp"),
        RegistrationPath: IoPath.Combine(folder, "helm-chart.adp"))
    {
        SubjectFolder = folder,
    };

    [Fact]
    public async Task ANestedChartsFindings_ArriveProjectRelative()
    {
        // Arrange.
        // A chart two levels down with a missing version: the finding must name
        // deploy/my-chart/Chart.yaml, not Chart.yaml - the exact bug class the sibling
        // module's unit tests missed.
        var chartFolder = IoPath.Combine(_root, "deploy", "my-chart");
        await File.WriteAllTextAsync(IoPath.Combine(chartFolder, "Chart.yaml"), "apiVersion: v2\nname: nested\n", TestContext.Current.CancellationToken);

        // Act.
        var problems = await new HelmValidator().ValidateAsync(Request(chartFolder), CancellationToken.None);

        // Assert.
        var missing = Assert.Single(problems, problem => problem.RuleId == HelmRules.MissingVersion);
        Assert.Equal(
            "deploy/my-chart/Chart.yaml",
            Assert.IsType<DiagramProblemFileLocation>(missing.Location).RelativePath);
    }

    [Fact]
    public async Task TheNotAChartFinding_LandsOnTheFolderItself()
    {
        // Arrange.
        // No Chart.yaml: the rule set blames "." and the validator must rebase that to the
        // folder's own project-relative path - never a literal "deploy/my-chart/.".
        var chartFolder = IoPath.Combine(_root, "deploy", "my-chart");

        // Act.
        var problems = await new HelmValidator().ValidateAsync(Request(chartFolder), CancellationToken.None);

        // Assert.
        var notAChart = Assert.Single(problems);
        Assert.Equal(HelmRules.NotAChart, notAChart.RuleId);
        Assert.Equal(
            "deploy/my-chart",
            Assert.IsType<DiagramProblemFileLocation>(notAChart.Location).RelativePath);
    }

    [Fact]
    public async Task AChartAtTheProjectRoot_KeepsItsPathsAsTheyAre()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "Chart.yaml"), "apiVersion: v2\nname: rooted\n", TestContext.Current.CancellationToken);

        // Act.
        var problems = await new HelmValidator().ValidateAsync(Request(_root), CancellationToken.None);

        // Assert.
        var missing = Assert.Single(problems, problem => problem.RuleId == HelmRules.MissingVersion);
        Assert.Equal("Chart.yaml", Assert.IsType<DiagramProblemFileLocation>(missing.Location).RelativePath);
    }

    [Fact]
    public async Task NoSubjectFolder_JudgesNothingRatherThanBlamingTheChart()
    {
        // Arrange.
        var request = Request(IoPath.Combine(_root, "deploy", "my-chart")) with { SubjectFolder = null };

        // Act.
        var problems = await new HelmValidator().ValidateAsync(request, CancellationToken.None);

        // Assert.
        Assert.Empty(problems);
    }
}
