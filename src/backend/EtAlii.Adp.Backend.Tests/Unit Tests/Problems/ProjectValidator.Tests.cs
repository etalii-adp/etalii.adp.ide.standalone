using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProjectValidatorTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", Extension: ".mm");

    private readonly string _root;

    public ProjectValidatorTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    // ---- what the walk finds -----------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ReportsWhatTheTypesValidatorFinds()
    {
        // Arrange.
        CreatePair("flow", "the document");
        var problem = new DiagramProblem(DiagramProblemSeverity.Warning, "The root is lonely.", "mindmap.lonely-root");
        var validator = Validator(problems: [problem]);

        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Same(problem, stored.Problem);
        Assert.Equal("flow.adp", stored.RelativePath);
        Assert.NotEqual(default, stored.LastWriteTimeUtc);
        Assert.NotEqual("", stored.RulesVersion);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_AFileLocatedProblem_IsAttributedAndPinnedToThatFile()
    {
        // Arrange.
        // A folder-subject type's rule names the file inside the folder that declared the
        // mistake. Attribution follows it so the panel reveals the right place, and - the part
        // that matters - the staleness pin follows it too: a folder diagram's .adp never
        // changes, so pinning to it would leave every verdict looking fresh for ever.
        CreatePair("flow", "the document");
        var declaring = IoPath.Combine(_root, "declaring.yml");
        await File.WriteAllTextAsync(declaring, "a file with something wrong in it\n", TestContext.Current.CancellationToken);
        var problem = new DiagramProblem(
            DiagramProblemSeverity.Error,
            "The role 'absent-role' has no folder.",
            "fixture.role-missing",
            new DiagramProblemFileLocation("declaring.yml", 6));

        // Act.
        var outcome = await Validate(Validator(problems: [problem]), new ProjectValidationScope(_root));

        // Assert.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("declaring.yml", stored.RelativePath);
        // Pinned to the declaring file's own stats, not the .adp's.
        Assert.Equal(new FileInfo(declaring).Length, stored.Length);
        Assert.Equal(new FileInfo(declaring).LastWriteTimeUtc, stored.LastWriteTimeUtc);
    }

    [Fact]
    public async Task ValidateAsync_AProblemLocationLeavingTheRoot_FallsBackToTheDiagramsOwnFile()
    {
        // Arrange.
        // The location comes from a module, and a module is trusted to stay inside the project
        // no more than a user-editable body: header is. Refused rather than followed - and the
        // problem is still reported, merely attributed less precisely.
        CreatePair("flow", "the document");
        var problem = new DiagramProblem(
            DiagramProblemSeverity.Error,
            "Something is wrong somewhere else entirely.",
            "fixture.escaping",
            new DiagramProblemFileLocation(IoPath.Combine("..", "..", "elsewhere.yml")));

        // Act.
        var outcome = await Validate(Validator(problems: [problem]), new ProjectValidationScope(_root));

        // Assert.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("flow.adp", stored.RelativePath);
    }

    [Fact]
    public async Task ValidateAsync_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange.
        // The handle every document store's save holds: FileMode.Create aside, this is the
        // mode File.WriteAllText opens with - write access, sharing only reads. Windows
        // sharing is mutual, so a validation read opened with plain FileShare.Read is refused
        // while this handle is open, and mirrored, an in-flight validation read makes the
        // editor's save fail - the intermittent "plan.tml could not be written" that
        // github-build-pipeline task 4.3's verification caught in TimelineFlowTests. This
        // pins the share mode that keeps Requirement 6.7 ("validation must never contend
        // with an editor") true; Unix does not enforce sharing, so the guard bites on
        // Windows, where the bug lived.
        CreatePair("flow", "the document");
        await using var editor = new FileStream(
            IoPath.Combine(_root, "flow.mm"), FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act: validate while the editor's write handle is open.
        var outcome = await Validate(Validator(problems: []), new ProjectValidationScope(_root));

        // Assert: read, routed and judged - no "could not be read" in sight.
        Assert.Empty(outcome.Problems);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_IsSilentForATypeWithoutRules()
    {
        // Arrange.
        CreatePair("flow", "the document");

        // Act.
        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        // Assert.
        Assert.Empty(outcome.Problems);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_JudgesAPairOnce_NotOncePerFile()
    {
        // Arrange.
        // flow.adp and flow.mm route to the same pair; the walk visits both files.
        CreatePair("flow", "the document");
        var validator = Validator(problems: []);

        // Act.
        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        // Assert.
        Assert.Equal(1, validator.Calls);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_ReportsAnUnknownType()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "strange.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.unknown-type", stored.Problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, stored.Problem.Severity);
        Assert.Contains("vendor/unheard-of", stored.Problem.Message);
        Assert.Equal("strange.adp", stored.RelativePath);
        Assert.Equal("", stored.RulesVersion);
    }

    [Fact]
    public async Task ValidateAsync_ReportsAnUnreadableRegistration()
    {
        // Arrange.
        // An empty .adp has no first line to read - the router calls it unreadable.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "empty.adp"), "", TestContext.Current.CancellationToken);

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.unreadable", stored.Problem.RuleId);
        Assert.Equal("empty.adp", stored.RelativePath);
    }

    [Fact]
    public async Task ValidateAsync_ReportsAnAmbiguousExtension()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "either.mm"), "a body without a registration", TestContext.Current.CancellationToken);
        var rival = new DiagramDefinition(new DiagramOrigin("rival", "mindmap"), "Rival map", Extension: ".mm");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root), extraDefinitions: [rival]);

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.ambiguous-extension", stored.Problem.RuleId);
        Assert.Contains("freeplane/mindmap", stored.Problem.Message);
        Assert.Contains("rival/mindmap", stored.Problem.Message);
    }

    [Fact]
    public async Task ValidateAsync_IgnoresAFileNoTypeClaims()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);

        // Act.
        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        // Assert.
        Assert.Empty(outcome.Problems);
        Assert.Equal(0, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_ReportsABodyItCannotRead_AndWalksOn()
    {
        // Arrange.
        CreatePair("locked", "unreachable");
        CreatePair("open", "reachable");
        var validator = Validator(problems: []);

        using (new FileStream(IoPath.Combine(_root, "locked.mm"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await Validate(validator, new ProjectValidationScope(_root));

        // Act and assert, step by step.
            var stored = Assert.Single(outcome.Problems);
            Assert.Equal("core.unreadable", stored.Problem.RuleId);
            Assert.Equal("locked.adp", stored.RelativePath);
        }
        // The lock cost that one file: the other pair was still validated.
        Assert.Equal(1, validator.Calls);
    }

    // ---- an untrusted module -----------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_SurvivesAThrowingValidator_AndWalksOn()
    {
        // Arrange.
        CreatePair("a", "first");
        CreatePair("b", "second");
        var validator = Validator(throwing: true);

        // Act.
        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        // Assert.
        Assert.Equal(2, outcome.Problems.Count);
        Assert.All(outcome.Problems, stored => Assert.Equal("core.validator-failed", stored.Problem.RuleId));
        Assert.Equal(2, validator.Calls);
    }

    [Fact]
    public async Task ValidateAsync_AbandonsAHangingValidator()
    {
        // Arrange.
        CreatePair("slow", "the document");
        var validator = Validator(hanging: true);

        var outcome = await Validate(validator, new ProjectValidationScope(_root), timeout: TimeSpan.FromMilliseconds(100));

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.validator-failed", stored.Problem.RuleId);
        Assert.Contains("did not answer", stored.Problem.Message);
    }

    // ---- scopes ------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_FolderScope_TouchesOnlyThatFolder()
    {
        // Arrange.
        Directory.CreateDirectory(IoPath.Combine(_root, "inside"));
        CreatePair(IoPath.Combine("inside", "in"), "inside the folder");
        CreatePair("out", "outside the folder");
        var validator = Validator(problems: [new DiagramProblem(DiagramProblemSeverity.Error, "Broken.", "mindmap.broken")]);

        var outcome = await Validate(validator, new FolderValidationScope(_root, "inside"));

        // Act and assert, step by step.
        var stored = Assert.Single(outcome.Problems);
        Assert.Equal(IoPath.Combine("inside", "in.adp"), stored.RelativePath);
        Assert.Equal(1, validator.Calls);
    }

    [Fact]
    public async Task ValidateAsync_FileScope_TouchesOnlyThatFile()
    {
        // Arrange.
        CreatePair("one", "first");
        CreatePair("two", "second");
        var validator = Validator(problems: []);

        // Act.
        var outcome = await Validate(validator, new FileValidationScope(_root, "one.adp"));

        // Assert.
        Assert.Equal(1, validator.Calls);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_RefusesAPathThatLeavesTheRoot()
    {
        // Arrange.
        var elsewhere = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        try
        {
            await File.WriteAllTextAsync(IoPath.Combine(elsewhere, "outside.adp"), "freeplane/mindmap\n", TestContext.Current.CancellationToken);
            var validator = Validator(problems: []);

        // Act.
            var outcome = await Validate(validator, new FileValidationScope(_root, IoPath.Combine("..", IoPath.GetFileName(elsewhere), "outside.adp")));

        // Assert.
            Assert.Empty(outcome.Problems);
            Assert.Equal(0, outcome.FilesConsidered);
            Assert.Equal(1, outcome.Skipped);
            Assert.Equal(0, validator.Calls);
        }
        finally
        {
            TestFolder.TryDelete(elsewhere);
        }
    }

    // ---- the project folder is sacred --------------------------------------------------

    [Fact]
    public async Task ValidateAsync_LeavesTheProjectByteForByteUnchanged()
    {
        // Arrange.
        CreatePair("flow", "the document");
        await File.WriteAllTextAsync(IoPath.Combine(_root, "strange.adp"), "vendor/unheard-of\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "notes.txt"), "just notes", TestContext.Current.CancellationToken);
        var before = Snapshot();

        await Validate(Validator(throwing: true), new ProjectValidationScope(_root));

        // Act and assert, step by step.
        var after = Snapshot();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, after[path]);
        }
    }

    // ---- one run per root --------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ASecondRequestJoinsTheRunningOne()
    {
        // Arrange.
        CreatePair("flow", "the document");
        var validator = Validator(problems: []);
        validator.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = Subject(validator);

        // Arrange, continued.
        var first = subject.ValidateAsync(new ProjectValidationScope(_root), TestContext.Current.CancellationToken).AsTask();
        while (validator.Calls == 0)
        {
            await Task.Yield(); // The first run is inside the validator - the second must join it.
        }
        var second = subject.ValidateAsync(new FileValidationScope(_root, "flow.adp"), TestContext.Current.CancellationToken).AsTask();

        // Act.
        validator.Hold.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        // Assert.
        Assert.Equal(1, validator.Calls); // One traversal...
        Assert.Same(outcomes[0], outcomes[1]); // ...two identical answers.
    }

    // ---- plumbing ----------------------------------------------------------------------

    // ---- registrations that name a body they do not own ---------------------------------

    [Fact]
    public async Task ValidateAsync_FollowsARegistrationsBodyHeader_RatherThanThrowingOnIt()
    {
        // Arrange.
        // Several diagrams over one document is the whole point of a C4 project, and every
        // registration but the owning one gets there through a `body:` header. Validating such a
        // project used to throw ArgumentException from deep inside the walk, because the router
        // was asked to resolve the header without being told which project root to resolve it
        // against - and handed back an empty path rather than refusing.
        CreatePair("shared", "the document");
        await File.WriteAllTextAsync(IoPath.Combine(_root, "second-view.adp"), "freeplane/mindmap\nbody: shared.mm\n", TestContext.Current.CancellationToken);
        var problem = new DiagramProblem(DiagramProblemSeverity.Warning, "The root is lonely.", "mindmap.lonely-root");

        // Act.
        var outcome = await Validate(Validator(problems: [problem]), new ProjectValidationScope(_root));

        // Assert.
        // One document, so one judgement: the pair and the second registration both resolve to
        // `shared.mm` and it is considered once.
        Assert.Single(outcome.Problems);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_ARegistrationWhoseBodyEscapesTheProject_IsReportedNotThrownOn()
    {
        // Arrange.
        // The header is user-editable text, so it may name anything at all. Refusing to follow it
        // is the documented behaviour (Requirement 2.4); throwing is not - and saying nothing
        // would leave a diagram that never opens with no explanation anywhere.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "escapee.adp"), "freeplane/mindmap\nbody: ../elsewhere.mm\n", TestContext.Current.CancellationToken);

        // Act.
        var outcome = await Validate(Validator(), new ProjectValidationScope(_root));

        // Assert.
        var problem = Assert.Single(outcome.Problems);
        Assert.Equal("escapee.adp", problem.RelativePath);
        // Named for what it is. This used to be reported as "The registration file could not be
        // read", which sends a reader looking for a permissions fault: the file read perfectly
        // well, it is the document it points at that is out of bounds.
        Assert.Contains("outside the project", problem.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be read", problem.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_ATypeThatKeepsNoDocument_IsStillConsidered()
    {
        // Arrange.
        // A type with no extension - c4/code is the real one - is its own whole diagram, so its
        // registration is what gets judged.
        var bodyless = new DiagramDefinition(new DiagramOrigin("fixture", "bodyless"), "Bodyless");
        await File.WriteAllTextAsync(IoPath.Combine(_root, "standalone.adp"), "fixture/bodyless\n", TestContext.Current.CancellationToken);
        // Act.
        var outcome = await Validate(Validator(), new ProjectValidationScope(_root), extraDefinitions: [bodyless]);

        // Assert.
        Assert.Equal(1, outcome.FilesConsidered);
    }


    private void CreatePair(string baseName, string body)
    {
        var adp = IoPath.Combine(_root, baseName + ".adp");
        Directory.CreateDirectory(IoPath.GetDirectoryName(adp)!);
        File.WriteAllText(adp, "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), body);
    }

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static ProjectValidatorTestValidator Validator(
        IReadOnlyList<DiagramProblem>? problems = null, bool throwing = false, bool hanging = false) =>
        new(Mindmap, problems ?? [], throwing, hanging);

    private async Task<ValidationOutcome> Validate(
        ProjectValidatorTestValidator? validator,
        ValidationScope scope,
        IReadOnlyList<DiagramDefinition>? extraDefinitions = null,
        TimeSpan? timeout = null) =>
        await Subject(validator, extraDefinitions, timeout).ValidateAsync(scope, TestContext.Current.CancellationToken);

    private static ProjectValidator Subject(
        ProjectValidatorTestValidator? validator,
        IReadOnlyList<DiagramDefinition>? extraDefinitions = null,
        TimeSpan? timeout = null)
    {
        var catalog = new TestDiagramDefinitionCatalog([MindmapDefinition, .. extraDefinitions ?? []]);
        var validators = new DiagramValidators(validator is null ? [] : [validator]);
        return new ProjectValidator(new DiagramFileRouter(catalog), validators, timeout);
    }


}
