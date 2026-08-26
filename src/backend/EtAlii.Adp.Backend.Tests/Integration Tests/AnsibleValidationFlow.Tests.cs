using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.AnsibleStructure;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Validation of a folder-subject diagram, end to end through core's own machinery.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the three core changes exist for, and the only place all of them are
/// exercised together by a real module: the validator is handed a <b>folder</b> (task 3), the
/// problem it reports is attributed and staleness-pinned to the file that <b>declared</b> the
/// mistake rather than to the <c>.adp</c> (tasks 4 and 5), and editing that file makes the
/// folder diagram revalidate rather than silently lose its problems (task 6).
/// </para>
/// <para>
/// Built from the DI container rather than over gRPC: the subject is core's validation and
/// maintenance plumbing, and putting a stream in front of it would test the stream.
/// </para>
/// </remarks>
public class AnsibleValidationFlowTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly string _infrastructure;
    private readonly ServiceProvider _provider;

    public AnsibleValidationFlowTests()
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        _infrastructure = IoPath.Combine(_projectFolder, "infrastructure");
        Directory.CreateDirectory(_projectFolder);

        AnsibleFixture.CopyBrokenTo(_infrastructure);
        File.WriteAllText(IoPath.Combine(_infrastructure, "infrastructure.adp"), "ansible/structure\n");

        var services = new ServiceCollection();
        services.AddSingleton<IDiagramDefinitionCatalog>(new TestDiagramDefinitionCatalog(EtAlii.Adp.Diagram.AnsibleStructure.Diagram.AnsibleStructure));
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
        services.AddAnsibleStructure();

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_appDataRoot))
        {
            Directory.Delete(_appDataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AFolderDiagramsProblems_AreAttributedToTheFilesThatDeclaredThem()
    {
        // Arrange.
        var validator = _provider.GetRequiredService<ProjectValidator>();

        // Act.
        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        // The validator was handed a folder rather than one MIME line, so it had something to
        // judge at all - task 3's whole reason for existing.
        var problem = Assert.Single(outcome.Problems);
        Assert.Equal("ansible.role-missing", problem.Problem.RuleId);
        Assert.Contains("absent-role", problem.Problem.Message, StringComparison.Ordinal);

        // And it points at the playbook that named it, not at the .adp that marks the folder.
        Assert.Equal(IoPath.Combine("infrastructure", "webservers.yml"), problem.RelativePath);
    }

    [Fact]
    public async Task AProblemsStaleness_IsPinnedToTheDeclaringFile_NotToTheRegistration()
    {
        // Arrange.
        var validator = _provider.GetRequiredService<ProjectValidator>();
        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);
        var problem = Assert.Single(outcome.Problems);

        // Assert.
        // The half that actually matters: a folder diagram's .adp never changes, so pinning to
        // it would leave every verdict looking fresh for ever.
        var declaring = new FileInfo(IoPath.Combine(_infrastructure, "webservers.yml"));
        Assert.Equal(declaring.LastWriteTimeUtc, problem.LastWriteTimeUtc);
        Assert.Equal(declaring.Length, problem.Length);
    }

    [Fact]
    public async Task EditingTheDeclaringFile_RefreshesTheDiagramRatherThanLosingItsProblems()
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
        // The user fixes the playbook in a text editor. webservers.yml is not a diagram and
        // never will be - without task 6's walk-up, core would validate it as itself, find
        // nothing, and CLEAR the diagram's problems without re-finding them.
        AnsibleFixture.RepairIn(_infrastructure);

        // Assert.
        var cleared = await WaitUntil(() => store.Get(_projectFolder).Problems.Count == 0);
        Assert.True(cleared, "The problem was still there after the file that declared it was fixed.");
    }

    [Fact]
    public async Task BreakingAFileAgain_BringsTheProblemBack()
    {
        // Arrange.
        // The other direction, and the one that proves the walk-up actually revalidates rather
        // than merely clearing: a problem has to be able to REAPPEAR from an edit to a file
        // that is not itself a diagram.
        var store = _provider.GetRequiredService<IProblemStore>();
        var validator = _provider.GetRequiredService<ProjectValidator>();
        using var maintenance = _provider.GetRequiredService<ProblemMaintenance>();

        AnsibleFixture.RepairIn(_infrastructure);
        var clean = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);
        store.Replace(_projectFolder, clean.Problems);
        Assert.Empty(store.Get(_projectFolder).Problems);

        maintenance.Track(_projectFolder);

        // Act.
        await File.WriteAllTextAsync(
            IoPath.Combine(_infrastructure, "webservers.yml"),
            "---\n- name: Configure the web tier\n  hosts: web\n  roles:\n    - common\n    - absent-role\n",
            TestContext.Current.CancellationToken);

        // Assert.
        var returned = await WaitUntil(() =>
            store.Get(_projectFolder).Problems.Any(problem => problem.Problem.RuleId == "ansible.role-missing"));
        Assert.True(returned, "The problem never came back after the file was broken again.");
    }

    [Fact]
    public async Task ValidatingAFolderDiagram_ChangesNothingInIt()
    {
        // Arrange.
        var validator = _provider.GetRequiredService<ProjectValidator>();
        var before = Directory.GetFiles(_infrastructure, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        // Act.
        await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        foreach (var (path, bytes) in before)
        {
            Assert.True(bytes.SequenceEqual(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)), $"{path} changed during validation.");
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
