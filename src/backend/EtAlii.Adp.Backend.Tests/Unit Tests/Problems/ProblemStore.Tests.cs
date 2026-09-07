using System.Reflection;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Problems;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProblemStoreTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

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
        TestFolder.TryDelete(scratch);
    }

    // ---- what an untouched project answers ---------------------------------------------

    [Fact]
    public void Get_AnswersNeverValidatedForAFreshProject()
    {
        // Arrange.
        using var store = Store();

        // Act.
        var set = store.Get(_root);

        // Assert.
        Assert.Equal(ProjectProblemSetState.NeverValidated, set.State);
        Assert.Empty(set.Problems);
        Assert.Equal(0, set.ErrorCount);
        Assert.Equal(0, set.WarningCount);
    }

    // ---- mutations ---------------------------------------------------------------------

    [Fact]
    public void Replace_MakesTheSetValidated_AndCounts()
    {
        // Arrange.
        using var store = Store();

        store.Replace(_root, [Problem("a.adp"), Problem("b.adp", DiagramProblemSeverity.Warning)]);

        // Act and assert, step by step.
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
        // Arrange.
        using var store = Store();

        // Act.
        store.Replace(_root, []);

        // Assert.
        Assert.Equal(ProjectProblemSetState.Validated, store.Get(_root).State);
    }

    [Fact]
    public void ReplaceFor_ReplacesOnlyTheCoveredPaths()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [
            Problem("kept.adp"),
            Problem("replaced.adp"),
            Problem(IoPath.Combine("folder", "inside.adp")),
        ]);

        // One file and one folder were re-validated; the folder is now clean.
        store.ReplaceFor(_root, ["replaced.adp", "folder"], [Problem("replaced.adp", message: "Still wrong.")]);

        // Act and assert, step by step.
        var set = store.Get(_root);
        Assert.Equal(2, set.Problems.Count);
        Assert.Contains(set.Problems, problem => problem.RelativePath == "kept.adp");
        Assert.Contains(set.Problems, problem => problem.Problem.Message == "Still wrong.");
    }

    [Fact]
    public void ReplaceFor_DoesNotSweepUpAPathThatMerelySharesAPrefix()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [Problem("folder2.adp")]);

        // Act.
        store.ReplaceFor(_root, ["folder"], []);

        // Assert.
        Assert.Single(store.Get(_root).Problems);
    }

    [Fact]
    public void Remove_DropsAFilesProblems()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [Problem("gone.adp"), Problem("stays.adp")]);

        store.Remove(_root, "gone.adp");

        // Act and assert, step by step.
        var set = store.Get(_root);
        Assert.Equal("stays.adp", Assert.Single(set.Problems).RelativePath);
    }

    [Fact]
    public void Remove_OfAFolder_DropsEverythingBeneathIt()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [Problem(IoPath.Combine("folder", "a.adp")), Problem(IoPath.Combine("folder", "deep", "b.adp")), Problem("outside.adp")]);

        // Act.
        store.Remove(_root, "folder");

        // Assert.
        Assert.Equal("outside.adp", Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Move_CarriesAFilesProblemsToTheNewPath()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [Problem("old.adp")]);

        // Act.
        store.Move(_root, "old.adp", "new.adp");

        // Assert.
        Assert.Equal("new.adp", Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Move_OfAFolder_CarriesEverythingBeneathIt()
    {
        // Arrange.
        using var store = Store();
        store.Replace(_root, [Problem(IoPath.Combine("before", "deep", "a.adp"))]);

        // Act.
        store.Move(_root, "before", "after");

        // Assert.
        Assert.Equal(IoPath.Combine("after", "deep", "a.adp"), Assert.Single(store.Get(_root).Problems).RelativePath);
    }

    [Fact]
    public void Changed_IsRaisedExactlyOncePerMutation()
    {
        // Arrange.
        using var store = Store();
        var raised = new List<string>();
        store.Changed += rootPath => raised.Add(rootPath);

        // Act.
        store.Replace(_root, [Problem("a.adp")]);
        store.ReplaceFor(_root, ["a.adp"], []);
        store.Remove(_root, "a.adp");
        store.Move(_root, "a.adp", "b.adp");

        // Assert.
        Assert.Equal(4, raised.Count);
        Assert.All(raised, rootPath => Assert.Equal(IoPath.GetFullPath(_root), rootPath));
    }

    // ---- staleness ---------------------------------------------------------------------

    [Fact]
    public async Task AFreshVerdictOnAPair_IsNotStale()
    {
        // Arrange.
        // Found by the manual pass: a pair's problem is attributed to the .adp, so its
        // pinned stats must be the .adp's too - pinning the body's stats marked every
        // pair's problem stale the moment it was found.
        CreatePair("flow");
        var validator = new ProblemStoreReportingValidator(Mindmap);
        using var store = Store(validator);
        var catalog = new TestDiagramDefinitionCatalog([MindmapDefinition]);
        var projectValidator = new ProjectValidator(new DiagramFileRouter(catalog), new DiagramValidators([validator]));

        var outcome = await projectValidator.ValidateAsync(new ProjectValidationScope(_root), TestContext.Current.CancellationToken);
        store.Replace(_root, outcome.Problems);

        // Act and assert, step by step.
        var stored = Assert.Single(store.Get(_root).Problems);
        Assert.Equal("flow.adp", stored.RelativePath);
        Assert.False(stored.Stale);
    }

    [Fact]
    public void Get_MarksNothingStaleWhileTheFileStandsStill()
    {
        // Arrange and act.
        using var store = Store();
        CreatePair("flow");
        store.Replace(_root, [Pinned("flow.adp")]);

        // Assert.
        Assert.False(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFileWasWrittenSince()
    {
        // Arrange.
        using var store = Store();
        CreatePair("flow");
        store.Replace(_root, [Pinned("flow.adp")]);

        // Act.
        File.SetLastWriteTimeUtc(IoPath.Combine(_root, "flow.adp"), DateTime.UtcNow.AddMinutes(1));

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFilesLengthChanged()
    {
        // Arrange.
        using var store = Store();
        CreatePair("flow");
        var path = IoPath.Combine(_root, "flow.adp");
        var pinned = Pinned("flow.adp");
        store.Replace(_root, [pinned]);

        // Act.
        File.AppendAllText(path, "more");
        File.SetLastWriteTimeUtc(path, pinned.LastWriteTimeUtc); // Only the length differs.

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenTheFileIsGone()
    {
        // Arrange and act.
        using var store = Store();
        store.Replace(_root, [Problem("vanished.adp")]);

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_MarksAnEntryStaleWhenItsRulesVersionIsNoLongerCurrent()
    {
        // Arrange.
        using var store = Store(new ProblemStoreStubValidator(Mindmap));
        CreatePair("flow");

        // Act.
        store.Replace(_root, [Pinned("flow.adp", rulesVersion: "an-older-release")]);

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    [Fact]
    public void Get_TrustsACurrentRulesVersion()
    {
        // Arrange.
        using var store = Store(new ProblemStoreStubValidator(Mindmap));
        CreatePair("flow");
        var current = typeof(ProblemStoreStubValidator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        // Act.
        store.Replace(_root, [Pinned("flow.adp", rulesVersion: current)]);

        // Assert.
        Assert.False(Assert.Single(store.Get(_root).Problems).Stale);
    }

    // ---- persistence -------------------------------------------------------------------

    [Fact]
    public void TheSetSurvivesARestart()
    {
        // Arrange.
        var location = new DiagramProblemElementLocation("node-1");
        using (var store = Store())
        {
            store.Replace(_root, [
                Problem("a.adp", location: location),
                Problem("b.adp", DiagramProblemSeverity.Warning),
            ]);
        } // Dispose keeps the pending debounced write.

        // Act.
        using var reborn = Store();
        var set = reborn.Get(_root);

        // Assert.
        Assert.Equal(ProjectProblemSetState.Validated, set.State);
        Assert.Equal(2, set.Problems.Count);
        Assert.Equal(1, set.ErrorCount);
        Assert.Equal(1, set.WarningCount);
        var kept = set.Problems.Single(problem => problem.RelativePath == "a.adp");
        Assert.Equal(location, kept.Problem.Location);
    }

    [Fact]
    public void AFileLocationSurvivesARestart_AndDoesNotDegradeIntoALineLocation()
    {
        // Arrange.
        // A folder-subject diagram's problem names a file inside the folder. The cache keeps
        // the two apart deliberately: a line location means "line N of the diagram's own
        // document", a file location means "line N of that other file", and collapsing them
        // would reopen the wrong file after a restart.
        var located = new DiagramProblemFileLocation("infrastructure/playbooks/deploy.yml", 6);
        using (var store = Store())
        {
            store.Replace(_root, [Problem("infrastructure.adp", location: located)]);
        } // Dispose keeps the pending debounced write.

        // Act.
        using var reborn = Store();
        var kept = Assert.Single(reborn.Get(_root).Problems);

        // Assert.
        Assert.Equal(located, kept.Problem.Location);
        Assert.IsType<DiagramProblemFileLocation>(kept.Problem.Location);
    }

    [Fact]
    public void AFileLocationWithoutALine_SurvivesARestart()
    {
        // Arrange.
        // An empty role folder has no line to point at; 0 must round-trip as 0 rather than
        // becoming a line-1 location that would look like a real position.
        var located = new DiagramProblemFileLocation("infrastructure/roles/hollow");
        using (var store = Store())
        {
            store.Replace(_root, [Problem("infrastructure.adp", location: located)]);
        }

        // Act.
        using var reborn = Store();
        var kept = Assert.Single(reborn.Get(_root).Problems);

        // Assert.
        Assert.Equal(located, kept.Problem.Location);
        Assert.Equal(0u, Assert.IsType<DiagramProblemFileLocation>(kept.Problem.Location).Line);
    }

    // ---- a subject that is a folder rather than a file ----------------------------------

    [Fact]
    public void AProblemLocatedAtAFolder_IsNotBornStale()
    {
        // Arrange.
        // Found by the ansible-structure-diagram manual pass: six of seven problems read fresh
        // and the one located at a FOLDER read stale, because both halves of the staleness
        // mechanism reached for FileInfo, whose Exists is false for a directory. A rule that
        // blames a folder - ansible.empty-role blames a role folder, having no file worth
        // blaming - was therefore telling the reader its verdict might be out of date the
        // instant it was computed.
        var folder = IoPath.Combine(_root, "roles", "hollow");
        Directory.CreateDirectory(folder);
        var stored = FolderProblem(folder);

        using var store = Store();

        // Act.
        store.Replace(_root, [stored]);

        // Assert.
        Assert.False(Assert.Single(store.Get(_root).Problems).Stale, "A folder-located problem was born stale.");
    }

    [Fact]
    public void AProblemLocatedAtAFolder_GoesStaleWhenTheFoldersOwnStampMoves()
    {
        // Arrange.
        // The other half: the fix must not simply make folder-located problems never stale.
        //
        // Asserted by moving the stamp rather than by touching the folder and hoping. A
        // folder's LastWriteTimeUtc only moves when a DIRECT child is added or removed, and
        // even then not on a schedule this test can depend on - an earlier version wrote a
        // file into the folder and asserted staleness, which is a clock test wearing a
        // filesystem's clothes. What is worth guarding is that a stamp mismatch is noticed;
        // what actually keeps a folder diagram current is the watcher walk-up, covered end to
        // end by AnsibleValidationFlowTests.
        var folder = IoPath.Combine(_root, "roles", "hollow");
        Directory.CreateDirectory(folder);
        using var store = Store();
        var pinned = FolderProblem(folder);
        store.Replace(_root, [pinned]);
        Assert.False(Assert.Single(store.Get(_root).Problems).Stale);

        // Act.
        // The same problem, judged against a folder whose stamp no longer matches.
        store.Replace(_root, [pinned with { LastWriteTimeUtc = pinned.LastWriteTimeUtc.AddSeconds(-30) }]);

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale, "A folder whose stamp moved did not go stale.");
    }

    [Fact]
    public void AProblemLocatedAtAFolderThatIsGone_IsStale()
    {
        // Arrange.
        var folder = IoPath.Combine(_root, "roles", "hollow");
        Directory.CreateDirectory(folder);
        using var store = Store();
        store.Replace(_root, [FolderProblem(folder)]);

        // Act.
        Directory.Delete(folder, recursive: true);

        // Assert.
        Assert.True(Assert.Single(store.Get(_root).Problems).Stale);
    }

    private StoredProblem FolderProblem(string folder) =>
        new(
            new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "The role 'hollow' has none of the folders Ansible looks in.",
                "fixture.empty-role",
                new DiagramProblemFileLocation("roles/hollow")),
            IoPath.GetRelativePath(_root, folder),
            new DirectoryInfo(folder).LastWriteTimeUtc,
            0,
            RulesVersion: "");

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
        // Arrange.
        using (var store = Store())
        {
            store.Replace(_root, [Problem("a.adp")]);
        }
        var cache = Assert.Single(Directory.GetFiles(IoPath.Combine(_appData, "EtAlii.Adp", "problems")));
        File.WriteAllText(cache, File.ReadAllText(cache).Replace("\"Version\": 1", "\"Version\": 999"));

        // Act.
        using var reborn = Store();

        // Assert.
        Assert.Equal(ProjectProblemSetState.NeverValidated, reborn.Get(_root).State);
    }

    [Fact]
    public void TheStoreNeverWritesInsideTheProjectFolder()
    {
        // Arrange.
        CreatePair("flow");
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();

        using (var store = Store())
        {
            store.Replace(_root, [Pinned("flow.adp")]);
            store.Get(_root);
        }

        // Act and assert, step by step.
        var after = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        Assert.Equal(before, after);
    }

    // ---- bounding ----------------------------------------------------------------------

    [Fact]
    public void Get_BoundsWhatItSends_ButCountsTheWholeSet()
    {
        // Arrange.
        using var store = Store(maxReported: 2);
        store.Replace(_root, [
            Problem("a.adp"),
            Problem("b.adp"),
            Problem("c.adp", DiagramProblemSeverity.Warning),
        ]);

        // Act.
        var set = store.Get(_root);

        // Assert.
        Assert.Equal(2, set.Problems.Count);
        Assert.Equal(2, set.TruncatedAt);
        Assert.Equal(2, set.ErrorCount); // Of the whole set...
        Assert.Equal(1, set.WarningCount); // ...not of what was sent.
    }

    // ---- plumbing ----------------------------------------------------------------------

    private ProblemStore Store(IDiagramValidator? validator = null, int maxReported = 1000)
    {
        var catalog = new TestDiagramDefinitionCatalog([MindmapDefinition]);
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


}
