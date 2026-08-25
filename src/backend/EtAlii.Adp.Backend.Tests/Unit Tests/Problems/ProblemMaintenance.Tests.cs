using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProblemMaintenanceTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _root;
    private readonly ProblemMaintenanceRecordingStore _store = new();
    private readonly ProblemMaintenanceCountingValidator _validator = new(Mindmap);
    private readonly ProblemMaintenance _maintenance;

    public ProblemMaintenanceTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog([MindmapDefinition]));
        var projectValidator = new ProjectValidator(router, new DiagramValidators([_validator]));
        _maintenance = new ProblemMaintenance(_store, projectValidator, router, SettleDelay);
    }

    public void Dispose()
    {
        _maintenance.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task AChangedDiagramCostsOneFilesValidation_NotATraversal()
    {
        // Arrange.
        CreatePair("a");
        CreatePair("b");
        _maintenance.Track(_root);

        File.AppendAllText(IoPath.Combine(_root, "a.mm"), " and more");

        // Act and assert, step by step.
        var (_, payload) = await WaitForMutation("ReplaceFor");
        // One file changed, one validation - project-wide traversal would have made two.
        Assert.Equal(1, _validator.Calls);
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.Contains("a.adp", covered);
        Assert.DoesNotContain(covered, path => path.StartsWith('b'));
    }

    [Fact]
    public async Task ARemovedDiagramLosesItsEntries()
    {
        // Arrange.
        CreatePair("gone");
        _maintenance.Track(_root);

        File.Delete(IoPath.Combine(_root, "gone.adp"));

        // Act and assert, step by step.
        var (_, payload) = await WaitForMutation("Remove");
        Assert.Equal("gone.adp", payload);
    }

    [Fact]
    public async Task ARenamedDiagramKeepsItsProblemsAtTheNewPath()
    {
        // Arrange.
        CreatePair("old");
        _maintenance.Track(_root);

        File.Move(IoPath.Combine(_root, "old.adp"), IoPath.Combine(_root, "new.adp"));

        // Act and assert, step by step.
        var (_, payload) = await WaitForMutation("Move");
        Assert.Equal(("old.adp", "new.adp"), payload);
        // Moved, never dropped: the problems were not re-reported as unchecked.
        Assert.DoesNotContain(_store.Mutations, mutation => mutation.Kind == "Remove");
    }

    [Fact]
    public async Task RapidChangesCoalesceIntoOneValidation()
    {
        // Arrange.
        CreatePair("busy");
        _maintenance.Track(_root);

        for (var edit = 0; edit < 5; edit++)
        {
            File.AppendAllText(IoPath.Combine(_root, "busy.mm"), " more");
        }

        // Act and assert, step by step.
        await WaitForMutation("ReplaceFor");
        await Task.Delay(SettleDelay + SettleDelay, TestContext.Current.CancellationToken);
        Assert.Equal(1, _validator.Calls);
    }

    [Fact]
    public async Task AChangeToAFileNoTypeClaims_IsIgnored()
    {
        // Arrange.
        CreatePair("real");
        _maintenance.Track(_root);

        File.WriteAllText(IoPath.Combine(_root, "notes.txt"), "just notes");

        // Act and assert, step by step.
        // The real pair's change proves events flow; the .txt must not have caused anything.
        File.AppendAllText(IoPath.Combine(_root, "real.mm"), " and more");
        var (_, payload) = await WaitForMutation("ReplaceFor");
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.DoesNotContain("notes.txt", covered);
        Assert.All(_store.Mutations, mutation => Assert.Equal("ReplaceFor", mutation.Kind));
    }

    // ---- plumbing ----------------------------------------------------------------------

    private void CreatePair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), "the document");
    }

    private async Task<(string Kind, object Payload)> WaitForMutation(string kind)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            var found = _store.Mutations.FirstOrDefault(mutation => mutation.Kind == kind);
            if (found.Kind is not null)
            {
                return found;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Fail($"No {kind} mutation arrived within {WaitLimit}.");
        return default;
    }

}
