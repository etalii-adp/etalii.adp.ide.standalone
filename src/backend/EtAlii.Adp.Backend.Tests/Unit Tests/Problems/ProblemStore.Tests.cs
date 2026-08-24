using System.Reflection;

using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProblemStoreTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _appData;
    private readonly string _root;

    public ProblemStoreTests()
    {
        var scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        _appData = IoPath.Combine(scratch, "appdata");
        _root = IoPath.Combine(scratch, "project");
        Directory.CreateDirectory(_appData);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        var scratch = IoPath.GetDirectoryName(_root)!;
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    // ---- what an untouched project answers ---------------------------------------------

    [Fact]
    public void Get_AnswersNeverValidatedForAFreshProject()
    {
        using var store = Store();

        var set = store.Get(_root);

        Assert.Equal(ProjectProblemSetState.NeverValidated, set.State);
        Assert.Empty(set.Problems);
        Assert.Equal(0, set.ErrorCount);
        Assert.Equal(0, set.WarningCount);
    }

    // ---- mutations ---------------------------------------------------------------------

    [Fact]
    public void Replace_MakesTheSetValidated_AndCounts()
    {
        using var store = Store();

        store.Replace(_root, [Problem("a.adp", DiagramProblemSeverity.Error), Problem("b.adp", DiagramProblemSeverity.Warning)]);

        var set = store.Get(_root);
        Assert.Equal(ProjectProblemSetState.Validated, set.State);
        Assert.Equal(2, set.Problems.Count);
        Assert.Equal(1, set.ErrorCount);
        Assert.Equal(1, set.WarningCount);
        Assert.Equal(0, set.TruncatedAt);
    }

    [Fact]
    public void Replace_WithNothing_IsValidatedAndClean_NotNeverValidated()
    {
        using var store = Store();

        store.Replace(_root, []);

        Assert.Equal(ProjectProblemSetState.Validated, store.Get(_root).State);
    }

    [Fact]
    public void ReplaceFor_ReplacesOnlyTheCoveredPaths()
    {
        using var store = Store();
        store.Replace(_root, [
            Problem("kept.adp"),
            Problem("replaced.adp"),
            Problem(IoPath.Combine("folder", "inside.adp")),
        ]);

        // One file and one folder were re-validated; the folder is now clean.
        store.ReplaceFor(_root, ["replaced.adp", "folder"], [Problem("replaced.adp", message: "Still wrong.")]);

        var set = store.Get(_root);
        Assert.Equal(2, set.Problems.Count);
        Assert.Contains(set.Problems, problem => problem.RelativePath == "kept.adp");
        Assert.Contains(set.Problems, problem => problem.Problem.Message == "Still wrong.");
    }

    [Fact]
    public void ReplaceFor_DoesNotSweepUpAPathThatMerelySharesAPrefix()
    {
        using var store = Store();
        store.Replace(_root, [Problem("folder2.adp")]);

        store.ReplaceFor(_root, ["folder"], []);

        Assert.Single(store.Get(_root).Problems);
    }

    [Fact]
    public void Remove_DropsAFilesProblems()
    {
        using var store = Store();
        store.Replace(_root, [Problem("gone.adp"), Problem("stays.adp")]);

        store.Remove(_root, "gone.adp");

        var set = store.Get(_root);
        Assert.Equal("stays.adp", Assert.Single(set.Problems).RelativePath);
    }

    [Fact]
    public void Remove_OfAFolder_DropsEverythingBeneathIt()
    {
        using var store = Store();
        store.Replace(_root, [Problem(IoPath.Combine("folder", "a.adp")), Problem(IoPath.Combine("folder", "deep", "b.adp")), Problem("outside.adp")]);

        store.Remove(_root, "folder");

        Assert.Equal("outside.adp", Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Move_CarriesAFilesProblemsToTheNewPath()
    {
        using var store = Store();
        store.Replace(_root, [Problem("old.adp")]);

        store.Move(_root, "old.adp", "new.adp");

        Assert.Equal("new.adp", Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Move_OfAFolder_CarriesEverythingBeneathIt()
    {
        using var store = Store();
        store.Replace(_root, [Problem(IoPath.Combine("before", "deep", "a.adp"))]);

        store.Move(_root, "before", "after");

        Assert.Equal(IoPath.Combine("after", "deep", "a.adp"), Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Changed_IsRaisedExactlyOncePerMutation()
    {
        using var store = Store();
        var raised = new List<string>();
        store.Changed += rootPath => raised.Add(rootPath);

        store.Replace(_root, [Problem("a.adp")]);
        store.ReplaceFor(_root, ["a.adp"], []);
        store.Remove(_root, "a.adp");
        store.Move(_root, "a.adp", "b.adp");

        Assert.Equal(4, raised.Count);
        Assert.All(raised, rootPath => Assert.Equal(IoPath.GetFullPath(_root), rootPath));
    }

    // ---- staleness ---------------------------------------------------------------------

    [Fact]
    public async Task AFreshVerdictOnAPair_IsNotStale()
    {
        // Found by the manual pass: a pair's problem is attributed to the .adp, so its
        // pinned stats must be the .adp's too - pinning the body's stats marked every
        // pair's problem stale the moment it was found.
        CreatePair("flow");
        var validator = new ReportingValidator(Mindmap);
        using var store = Store(validator);
        var catalog = new TestCatalog([MindmapDefinition]);
        var projectValidator = new ProjectValidator(new DiagramFileRouter(catalog), new DiagramValidators([validator]));

        var outcome = await projectValidator.ValidateAsync(new ValidationScope.Project(_root), TestContext.Current.CancellationToken);
        store.Replace(_root, outcome.Problems);

        var stored = Assert.Single(store.Get(_root).Problems);
        Assert.Equal("flow.adp", stored.RelativePath);
        Assert.False(stored.Stale);
    }

    [Fact]
    public void Get_MarksNothingStaleWhileTheFileStandsStill()
    {
        using var store = Store();
        CreatePair("flow");
        store.Replace(_root, [Pinned("flow.adp")]);

        Assert.False(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFileWasWrittenSince()
    {
        using var store = Store();
        CreatePair("flow");
        store.Replace(_root, [Pinned("flow.adp")]);

        File.SetLastWriteTimeUtc(IoPath.Combine(_root, "flow.adp"), DateTime.UtcNow.AddMinutes(1));

        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFilesLengthChanged()
    {
        using var store = Store();
        CreatePair("flow");
        var path = IoPath.Combine(_root, "flow.adp");
        var pinned = Pinned("flow.adp");
        store.Replace(_root, [pinned]);

        File.AppendAllText(path, "more");
        File.SetLastWriteTimeUtc(path, pinned.LastWriteTimeUtc); // Only the length differs.

        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFileIsGone()
    {
        using var store = Store();
        store.Replace(_root, [Problem("vanished.adp")]);

        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenItsRulesVersionIsNoLongerCurrent()
    {
        using var store = Store(new StubValidator(Mindmap));
        CreatePair("flow");

        store.Replace(_root, [Pinned("flow.adp", rulesVersion: "an-older-release")]);

        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_TrustsACurrentRulesVersion()
    {
        using var store = Store(new StubValidator(Mindmap));
        CreatePair("flow");
        var current = typeof(StubValidator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        store.Replace(_root, [Pinned("flow.adp", rulesVersion: current)]);

        Assert.False(Assert.Single(store.Get(_root).Problems).Stale);
    }

    // ---- persistence -------------------------------------------------------------------

    [Fact]
    public void TheSetSurvivesARestart()
    {
        var location = new DiagramProblemLocation.ElementId("node-1");
        using (var store = Store())
        {
            store.Replace(_root, [
                Problem("a.adp", DiagramProblemSeverity.Error, location: location),
                Problem("b.adp", DiagramProblemSeverity.Warning),
            ]);
        } // Dispose keeps the pending debounced write.

        using var reborn = Store();
        var set = reborn.Get(_root);

        Assert.Equal(ProjectProblemSetState.Validated, set.State);
        Assert.Equal(2, set.Problems.Count);
        Assert.Equal(1, set.ErrorCount);
        Assert.Equal(1, set.WarningCount);
        var kept = set.Problems.Single(problem => problem.RelativePath == "a.adp");
        Assert.Equal(location, kept.Problem.Location);
    }

    [Fact]
    public void ACorruptCacheYieldsNeverValidated_NotAThrow()
    {
        using (var store = Store())
        {
            store.Replace(_root, [Problem("a.adp")]);
        }
        var cache = Assert.Single(Directory.GetFiles(IoPath.Combine(_appData, "EtAlii.Adp", "problems")));
        File.WriteAllText(cache, "{ not json");

        using var reborn = Store();
        var set = reborn.Get(_root);

        Assert.Equal(ProjectProblemSetState.NeverValidated, set.State);
        Assert.Empty(set.Problems);
    }

    [Fact]
    public void ACacheFromAnotherFormatYieldsNeverValidated()
    {
        using (var store = Store())
        {
            store.Replace(_root, [Problem("a.adp")]);
        }
        var cache = Assert.Single(Directory.GetFiles(IoPath.Combine(_appData, "EtAlii.Adp", "problems")));
        File.WriteAllText(cache, File.ReadAllText(cache).Replace("\"Version\": 1", "\"Version\": 999"));

        using var reborn = Store();

        Assert.Equal(ProjectProblemSetState.NeverValidated, reborn.Get(_root).State);
    }

    [Fact]
    public void TheStoreNeverWritesInsideTheProjectFolder()
    {
        CreatePair("flow");
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();

        using (var store = Store())
        {
            store.Replace(_root, [Pinned("flow.adp")]);
            store.Get(_root);
        }

        var after = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        Assert.Equal(before, after);
    }

    // ---- bounding ----------------------------------------------------------------------

    [Fact]
    public void Get_BoundsWhatItSends_ButCountsTheWholeSet()
    {
        using var store = Store(maxReported: 2);
        store.Replace(_root, [
            Problem("a.adp", DiagramProblemSeverity.Error),
            Problem("b.adp", DiagramProblemSeverity.Error),
            Problem("c.adp", DiagramProblemSeverity.Warning),
        ]);

        var set = store.Get(_root);

        Assert.Equal(2, set.Problems.Count);
        Assert.Equal(2, set.TruncatedAt);
        Assert.Equal(2, set.ErrorCount); // Of the whole set...
        Assert.Equal(1, set.WarningCount); // ...not of what was sent.
    }

    // ---- plumbing ----------------------------------------------------------------------

    private ProblemStore Store(IDiagramValidator? validator = null, int maxReported = 1000)
    {
        var catalog = new TestCatalog([MindmapDefinition]);
        return new ProblemStore(
            _appData,
            new DiagramFileRouter(catalog),
            new DiagramValidators(validator is null ? [] : [validator]),
            writeDelay: TimeSpan.FromMinutes(5), // Only Dispose writes - the tests stay deterministic.
            maxReported);
    }

    private void CreatePair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), "the document");
    }

    private static StoredProblem Problem(
        string relativePath,
        DiagramProblemSeverity severity = DiagramProblemSeverity.Error,
        string message = "Something is wrong.",
        DiagramProblemLocation? location = null) =>
        new(new DiagramProblem(severity, message, "test.rule", location), relativePath, DateTime.UtcNow, 1, RulesVersion: "");

    /// <summary>A problem pinned to the file as it is on disk right now - not stale by construction.</summary>
    private StoredProblem Pinned(string relativePath, string? rulesVersion = null)
    {
        var info = new FileInfo(IoPath.Combine(_root, relativePath));
        return new StoredProblem(
            new DiagramProblem(DiagramProblemSeverity.Error, "Something is wrong.", "test.rule"),
            relativePath,
            info.LastWriteTimeUtc,
            info.Length,
            rulesVersion ?? "");
    }

    private sealed class TestCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }

    private sealed class StubValidator(DiagramOrigin origin) : IDiagramValidator
    {
        public DiagramOrigin Origin { get; } = origin;

        public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
            string document, string baseName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
    }

    private sealed class ReportingValidator(DiagramOrigin origin) : IDiagramValidator
    {
        public DiagramOrigin Origin { get; } = origin;

        public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
            string document, string baseName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DiagramProblem>>(
                [new DiagramProblem(DiagramProblemSeverity.Warning, "Something to remember.", "test.remember")]);
    }
}
