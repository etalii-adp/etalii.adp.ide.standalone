using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.HelmCharts;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Problems;
using EtAlii.Adp.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Validation of the second folder-subject diagram, end to end through core's own machinery:
/// the validator handed a folder, findings attributed project-relative to the files that
/// declared them, and the walk-up revalidating the chart when a file inside it changes.
/// </summary>
/// <remarks>
/// Built from the DI container rather than over gRPC: the subject is core's validation and
/// maintenance plumbing, and putting a stream in front of it would test the stream.
/// </remarks>
public class HelmValidationFlowTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly string _chart;
    private readonly ServiceProvider _provider;

    public HelmValidationFlowTests()
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        _chart = IoPath.Combine(_projectFolder, "web-shop");
        Directory.CreateDirectory(_projectFolder);

        HelmFixture.CopyBrokenTo(_chart);
        File.WriteAllText(IoPath.Combine(_chart, "web-shop.adp"), "helm/chart\r\n");

        var services = new ServiceCollection();
        services.AddSingleton<IDiagramDefinitionCatalog>(
            new TestDiagramDefinitionCatalog(EtAlii.Adp.Diagram.HelmCharts.Diagram.HelmCharts));
        services.AddSingleton<DiagramFileRouter>();
        services.AddSingleton<DiagramValidators>();
        services.AddSingleton<ProjectValidator>();
        services.AddSingleton<IProblemStore>(provider => new ProblemStore(
            _appDataRoot, provider.GetRequiredService<DiagramFileRouter>(), provider.GetRequiredService<DiagramValidators>()));
        services.AddSingleton(provider => new ProblemMaintenance(
            provider.GetRequiredService<IProblemStore>(),
            provider.GetRequiredService<ProjectValidator>(),
            provider.GetRequiredService<DiagramFileRouter>(),
            SettleDelay));
        services.AddHelmCharts();

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task AChartsProblems_ArriveProjectRelative_AtTheFileThatDeclaredThem()
    {
        // Arrange.
        var validator = _provider.GetRequiredService<ProjectValidator>();

        // Act.
        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        // One mistake, one finding - attributed to the Chart.yaml that lacks the version,
        // project-relative (the nested-chart rebasing, exercised through core rather than the
        // module's own tests).
        var problem = Assert.Single(outcome.Problems);
        Assert.Equal(HelmRules.MissingVersion, problem.Problem.RuleId);
        Assert.Equal(IoPath.Combine("web-shop", "Chart.yaml"), problem.RelativePath);
    }

    [Fact]
    public async Task EditingTheDeclaringFile_RefreshesTheChartRatherThanLosingItsProblems()
    {
        // Arrange.
        var store = _provider.GetRequiredService<IProblemStore>();
        var validator = _provider.GetRequiredService<ProjectValidator>();
        using var maintenance = _provider.GetRequiredService<ProblemMaintenance>();

        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);
        store.Replace(_projectFolder, outcome.Problems);
        Assert.Single(store.Get(_projectFolder).Problems);

        maintenance.Track(_projectFolder);

        // Act.
        // The user fixes Chart.yaml in a text editor. Without the folder walk-up, core would
        // validate the file as itself, find nothing, and clear the chart's problems without
        // re-finding them.
        HelmFixture.RepairIn(_chart);

        // Assert.
        var cleared = await WaitUntil(() => store.Get(_projectFolder).Problems.Count == 0);
        Assert.True(cleared, "The problem was still there after the file that declared it was fixed.");
    }

    [Fact]
    public async Task BreakingAFileAgain_BringsTheProblemBack()
    {
        // Arrange.
        var store = _provider.GetRequiredService<IProblemStore>();
        var validator = _provider.GetRequiredService<ProjectValidator>();
        using var maintenance = _provider.GetRequiredService<ProblemMaintenance>();

        HelmFixture.RepairIn(_chart);
        var clean = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);
        store.Replace(_projectFolder, clean.Problems);
        Assert.Empty(store.Get(_projectFolder).Problems);

        maintenance.Track(_projectFolder);

        // Act.
        HelmFixture.CopyBrokenTo(_chart);

        // Assert.
        var returned = await WaitUntil(() =>
            store.Get(_projectFolder).Problems.Any(problem => problem.Problem.RuleId == HelmRules.MissingVersion));
        Assert.True(returned, "The problem never came back after the file was broken again.");
    }

    [Fact]
    public async Task ValidatingAChart_ChangesNothingInIt()
    {
        // Arrange.
        var validator = _provider.GetRequiredService<ProjectValidator>();
        var before = Directory.GetFiles(_chart, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        // Act.
        await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        foreach (var (path, bytes) in before)
        {
            var bytesToCheck = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(bytesToCheck), $"{path} changed during validation.");
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return false;
    }
}
