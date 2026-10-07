using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Problems.Tests;

public class ProblemMaintenanceTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

    private static readonly DiagramOrigin FolderType = new("fixture", "folder");
    private static readonly DiagramDefinition FolderDefinition =
        new(FolderType, "A folder-subject type", Subject: DiagramSubject.Folder);

    private readonly string _root;
    private readonly ProblemMaintenanceRecordingStore _store = new();
    private readonly ProblemMaintenanceCountingValidator _validator = new(Mindmap);
    private readonly ProblemMaintenanceFolderValidator _folderValidator =
        new(FolderType, IoPath.Combine("infrastructure", "roles", "web", "meta", "main.yml"));
    private readonly ProblemMaintenance _maintenance;

    public ProblemMaintenanceTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog(MindmapDefinition, FolderDefinition));
        var projectValidator = new ProjectValidator(router, new DiagramValidators([_validator, _folderValidator]));
        _maintenance = new ProblemMaintenance(_store, projectValidator, router, SettleDelay);
    }

    public void Dispose()
    {
        _maintenance.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task AChangedDiagramCostsOneFilesValidation_NotATraversal()
    {
        // Arrange.
        CreatePair("a");
        CreatePair("b");
        _maintenance.Track(_root);

        await File.AppendAllTextAsync(IoPath.Combine(_root, "a.mm"), " and more", TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        (_, object payload) = await WaitForMutation("ReplaceFor");
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
        (_, object payload) = await WaitForMutation("Remove");
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
        (_, object payload) = await WaitForMutation("Move");
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
            await File.AppendAllTextAsync(IoPath.Combine(_root, "busy.mm"), " more", TestContext.Current.CancellationToken);
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

        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        // The real pair's change proves events flow; the .txt must not have caused anything.
        await File.AppendAllTextAsync(IoPath.Combine(_root, "real.mm"), " and more", TestContext.Current.CancellationToken);
        (_, object payload) = await WaitForMutation("ReplaceFor");
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.DoesNotContain("notes.txt", covered);
        Assert.All(_store.Mutations, mutation => Assert.Equal("ReplaceFor", mutation.Kind));
    }

    [Fact]
    public async Task AChangeInsideAFolderDiagram_RevalidatesThatDiagram()
    {
        // Arrange.
        // The file edited is not a diagram and never will be. It is part of one: the .adp two
        // levels above it marks the folder as the diagram's subject. Without the walk-up the
        // change would be validated as itself, find nothing, and clear the diagram's problems
        // without re-finding them - losing a problem rather than refreshing it.
        var folder = IoPath.Combine(_root, "infrastructure");
        var meta = IoPath.Combine(folder, "roles", "web", "meta");
        Directory.CreateDirectory(meta);
        await File.WriteAllTextAsync(IoPath.Combine(folder, "infrastructure.adp"), "fixture/folder\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(meta, "main.yml"), "dependencies: [base]\n", TestContext.Current.CancellationToken);
        _maintenance.Track(_root);

        // Act.
        await File.AppendAllTextAsync(IoPath.Combine(meta, "main.yml"), "# edited\n", TestContext.Current.CancellationToken);

        // Assert.
        (_, object payload) = await WaitForMutation("ReplaceFor");
        Assert.Equal(1, _folderValidator.Calls);
        // The validator was handed the folder, not just one MIME line of registration text.
        Assert.Equal(folder, _folderValidator.LastSubjectFolder);
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.Contains(IoPath.Combine("infrastructure", "infrastructure.adp"), covered);
        Assert.Contains(IoPath.Combine("infrastructure", "roles", "web", "meta", "main.yml"), covered);
    }

    [Fact]
    public async Task AChangeOutsideAnyFolderDiagram_IsStillIgnored()
    {
        // Arrange.
        // The walk-up must not turn every stray file into a validation: this one has no
        // folder-subject registration above it, so it stays as ignorable as it always was.
        CreatePair("real");
        _maintenance.Track(_root);

        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);
        await Task.Delay(SettleDelay + SettleDelay, TestContext.Current.CancellationToken);

        // Act.
        await File.AppendAllTextAsync(IoPath.Combine(_root, "real.mm"), " and more", TestContext.Current.CancellationToken);

        // Assert.
        await WaitForMutation("ReplaceFor");
        Assert.Equal(0, _folderValidator.Calls);
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
