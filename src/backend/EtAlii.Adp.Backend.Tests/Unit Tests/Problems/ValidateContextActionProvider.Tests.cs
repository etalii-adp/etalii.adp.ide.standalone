using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ValidateContextActionProviderTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _root;
    private readonly ProblemStore _store;
    private readonly ProjectValidator _projectValidator;
    private readonly ValidateContextActionProvider _provider;
    private readonly ValidateAllContextActionProvider _validateAll;

    public ValidateContextActionProviderTests()
    {
        var scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        _root = IoPath.Combine(scratch, "project");
        Directory.CreateDirectory(_root);
        var router = new DiagramFileRouter(new TestDiagramDefinitionCatalog([MindmapDefinition]));
        var validators = new DiagramValidators([]);
        _store = new ProblemStore(IoPath.Combine(scratch, "appdata"), router, validators, writeDelay: TimeSpan.FromMinutes(5));
        _projectValidator = new ProjectValidator(router, validators);
        _provider = new ValidateContextActionProvider(_store, _projectValidator, router);
        _validateAll = new ValidateAllContextActionProvider(_store, _projectValidator);
    }

    public void Dispose()
    {
        _store.Dispose();
        var scratch = IoPath.GetDirectoryName(_root)!;
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    // ---- what is offered where ---------------------------------------------------------

    [Fact]
    public async Task AFolder_IsOfferedValidateFolder_WithF6()
    {
        var folder = IoPath.Combine(_root, "docs");
        Directory.CreateDirectory(folder);

        var groups = await Discover(FolderTarget(folder));

        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.Equal(ValidateContextActionProvider.ValidateActionId, action.Id);
        Assert.Equal("Validate folder", action.Label);
        Assert.Equal("mdi-check-circle-outline", action.Icon);
        Assert.Equal(new ContextShortcutDefinition("F6"), action.Shortcut);
        Assert.True(action.Available);
    }

    [Fact]
    public async Task ADiagram_IsOfferedValidate()
    {
        CreatePair("flow");

        var groups = await Discover(FileTarget(IoPath.Combine(_root, "flow.adp")));

        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.Equal("Validate", action.Label);
    }

    [Fact]
    public async Task AFileNoTypeClaims_GetsNoEntryAtAll_NotAGreyedOne()
    {
        var path = IoPath.Combine(_root, "notes.txt");
        File.WriteAllText(path, "just notes");

        var groups = await Discover(FileTarget(path));

        Assert.Empty(groups);
    }

    [Fact]
    public async Task AVanishedTarget_GetsNoEntry()
    {
        var groups = await Discover(FileTarget(IoPath.Combine(_root, "gone.adp")));

        Assert.Empty(groups);
    }

    // ---- what executing does -----------------------------------------------------------

    [Fact]
    public async Task Validate_OnADiagram_PutsItsProblemsInTheStore()
    {
        File.WriteAllText(IoPath.Combine(_root, "strange.adp"), "vendor/unheard-of\n");

        var result = await _provider.ExecuteAsync(FileTarget(IoPath.Combine(_root, "strange.adp")), ValidateContextActionProvider.ValidateActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        var set = _store.Get(_root);
        Assert.Equal(ProjectProblemSetState.Validated, set.State);
        Assert.Equal("core.unknown-type", Assert.Single(set.Problems).Problem.RuleId);
    }

    [Fact]
    public async Task Validate_OnAFolder_TouchesOnlyThatFolder()
    {
        Directory.CreateDirectory(IoPath.Combine(_root, "inside"));
        File.WriteAllText(IoPath.Combine(_root, "inside", "bad.adp"), "vendor/unheard-of\n");
        File.WriteAllText(IoPath.Combine(_root, "outside.adp"), "vendor/unheard-of\n");
        // The outside file's stale verdict must survive a folder-scoped validation untouched.
        _store.Replace(_root, [new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Warning, "Old verdict.", "test.old"), "outside.adp", DateTime.UtcNow, 1, "")]);

        await _provider.ExecuteAsync(FolderTarget(IoPath.Combine(_root, "inside")), ValidateContextActionProvider.ValidateActionId, TestContext.Current.CancellationToken);

        var set = _store.Get(_root);
        Assert.Equal(2, set.Problems.Count);
        Assert.Contains(set.Problems, problem => problem.Problem.RuleId == "test.old");
        Assert.Contains(set.Problems, problem => problem.RelativePath == IoPath.Combine("inside", "bad.adp"));
    }

    [Fact]
    public async Task Validate_OnTheRoot_IsValidateAll()
    {
        File.WriteAllText(IoPath.Combine(_root, "bad.adp"), "vendor/unheard-of\n");
        // A verdict for a file that no longer exists: a whole-project run must sweep it out.
        _store.Replace(_root, [new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Warning, "Old verdict.", "test.old"), "vanished.adp", DateTime.UtcNow, 1, "")]);

        await _provider.ExecuteAsync(RootTarget(), ValidateContextActionProvider.ValidateActionId, TestContext.Current.CancellationToken);

        var set = _store.Get(_root);
        Assert.Equal("core.unknown-type", Assert.Single(set.Problems).Problem.RuleId);
    }

    [Fact]
    public async Task Validate_OnAVanishedTarget_FailsWithoutWriting()
    {
        var result = await _provider.ExecuteAsync(FileTarget(IoPath.Combine(_root, "gone.adp")), ValidateContextActionProvider.ValidateActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionFailed>(result);
        Assert.Equal(ProjectProblemSetState.NeverValidated, _store.Get(_root).State);
    }

    // ---- Validate all ------------------------------------------------------------------

    [Fact]
    public async Task ValidateAll_IsAlwaysOffered_EvenWithNoValidators()
    {
        var groups = await _validateAll.DiscoverAsync(PanelTarget(), TestContext.Current.CancellationToken);

        var action = Assert.Single(Assert.Single(groups).Actions);
        Assert.Equal(ValidateAllContextActionProvider.ValidateAllActionId, action.Id);
        Assert.Equal("Validate all", action.Label);
        Assert.Equal("mdi-check-all", action.Icon);
        Assert.Equal(new ContextShortcutDefinition("B", Ctrl: true, Shift: true), action.Shortcut);
        Assert.True(action.Available);
    }

    [Fact]
    public async Task ValidateAll_ValidatesTheWholeProject()
    {
        File.WriteAllText(IoPath.Combine(_root, "bad.adp"), "vendor/unheard-of\n");

        var result = await _validateAll.ExecuteAsync(PanelTarget(), ValidateAllContextActionProvider.ValidateAllActionId, TestContext.Current.CancellationToken);

        Assert.IsType<ContextExecutionCompleted>(result);
        var set = _store.Get(_root);
        Assert.Equal(ProjectProblemSetState.Validated, set.State);
        Assert.Equal(1, set.ErrorCount);
    }

    [Fact]
    public async Task NeitherProvider_AnswersAnUnknownActionId()
    {
        Assert.IsType<ContextExecutionFailed>(
            await _provider.ExecuteAsync(RootTarget(), "problems.other", TestContext.Current.CancellationToken));
        Assert.IsType<ContextExecutionFailed>(
            await _validateAll.ExecuteAsync(PanelTarget(), "problems.other", TestContext.Current.CancellationToken));
    }

    // ---- plumbing ----------------------------------------------------------------------

    private void CreatePair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), "the document");
    }

    private async Task<IReadOnlyList<ContextActionGroupDefinition>> Discover(ContextTarget target) =>
        await _provider.DiscoverAsync(target, TestContext.Current.CancellationToken);

    private ContextTarget RootTarget() =>
        new(ContextScope.Hierarchy, _root, IsContainer: true, SourceId: default, RootPath: _root);

    private ContextTarget FolderTarget(string fullPath) =>
        new(ContextScope.Hierarchy, fullPath, IsContainer: true, SourceId: ShortGuid.NewShortGuid(), RootPath: _root);

    private ContextTarget FileTarget(string fullPath) =>
        new(ContextScope.Hierarchy, fullPath, IsContainer: false, SourceId: ShortGuid.NewShortGuid(), RootPath: _root);

    private ContextTarget PanelTarget() =>
        new(ContextScope.ProblemsPanel, _root, IsContainer: false, SourceId: default, RootPath: _root);

}
