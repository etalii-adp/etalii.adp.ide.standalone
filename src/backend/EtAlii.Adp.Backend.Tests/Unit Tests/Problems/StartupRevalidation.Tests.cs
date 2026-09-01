using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class StartupRevalidationTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

    private readonly string _scratch;
    private readonly StartupRevalidationRecordingStore _store = new();
    private readonly StartupRevalidationGatedValidator _validator = new(Mindmap);
    private readonly ProjectValidator _projectValidator;

    public StartupRevalidationTests()
    {
        _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog([MindmapDefinition]));
        _projectValidator = new ProjectValidator(router, new DiagramValidators([_validator]));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_scratch);
    }

    [Fact]
    public async Task EveryKnownProjectIsRevalidated_Sequentially()
    {
        // Arrange.
        var first = CreateProject("first");
        var second = CreateProject("second");
        _store.Roots = [first, second];
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        // Act.
        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([first, second], _store.Replaced);
    }

    [Fact]
    public async Task AnOpenedProjectJumpsTheQueue()
    {
        // Arrange.
        var first = CreateProject("first");
        var second = CreateProject("second");
        var third = CreateProject("third");
        _store.Roots = [first, second, third];
        _validator.HoldFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        // Act.
        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        while (_validator.Calls == 0)
        {
            await Task.Yield(); // The first project is mid-validation - the moment to open another.
        }
        revalidation.Prioritize(third);
        _validator.HoldFirstCall.SetResult();
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        // Assert.
        // The opened project's freshness is the one the user can see.
        Assert.Equal([first, third, second], _store.Replaced);
    }

    [Fact]
    public async Task AMissingRootIsSkippedOnce_NeverRetried()
    {
        // Arrange.
        var present = CreateProject("present");
        var missing = IoPath.Combine(_scratch, "not-there");
        _store.Roots = [missing, present];
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        // Act.
        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        // Assert.
        // The queue drained - a retry loop would never have let Completion finish.
        Assert.Equal([present], _store.Replaced);
    }

    [Fact]
    public async Task StoppingAbandonsTheQueue()
    {
        // Arrange.
        var first = CreateProject("first");
        var second = CreateProject("second");
        _store.Roots = [first, second];
        _validator.HoldFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        // Act.
        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        while (_validator.Calls == 0)
        {
            await Task.Yield();
        }
        var stopping = revalidation.StopAsync(TestContext.Current.CancellationToken);
        _validator.HoldFirstCall.SetResult();
        await stopping;

        // Assert.
        Assert.DoesNotContain(second, _store.Replaced);
    }

    // ---- plumbing ----------------------------------------------------------------------

    private string CreateProject(string name)
    {
        var root = IoPath.Combine(_scratch, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(IoPath.Combine(root, "flow.adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(root, "flow.mm"), "the document");
        return root;
    }

}
