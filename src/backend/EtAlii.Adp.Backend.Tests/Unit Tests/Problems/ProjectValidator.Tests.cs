using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProjectValidatorTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _root;

    public ProjectValidatorTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- what the walk finds -----------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ReportsWhatTheTypesValidatorFinds()
    {
        CreatePair("flow", "the document");
        var problem = new DiagramProblem(DiagramProblemSeverity.Warning, "The root is lonely.", "mindmap.lonely-root");
        var validator = Validator(problems: [problem]);

        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        var stored = Assert.Single(outcome.Problems);
        Assert.Same(problem, stored.Problem);
        Assert.Equal("flow.adp", stored.RelativePath);
        Assert.NotEqual(default, stored.LastWriteTimeUtc);
        Assert.NotEqual("", stored.RulesVersion);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_IsSilentForATypeWithoutRules()
    {
        CreatePair("flow", "the document");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        Assert.Empty(outcome.Problems);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_JudgesAPairOnce_NotOncePerFile()
    {
        // flow.adp and flow.mm route to the same pair; the walk visits both files.
        CreatePair("flow", "the document");
        var validator = Validator(problems: []);

        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        Assert.Equal(1, validator.Calls);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_ReportsAnUnknownType()
    {
        File.WriteAllText(IoPath.Combine(_root, "strange.adp"), "vendor/unheard-of\n");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

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
        // An empty .adp has no first line to read - the router calls it unreadable.
        File.WriteAllText(IoPath.Combine(_root, "empty.adp"), "");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.unreadable", stored.Problem.RuleId);
        Assert.Equal("empty.adp", stored.RelativePath);
    }

    [Fact]
    public async Task ValidateAsync_ReportsAnAmbiguousExtension()
    {
        File.WriteAllText(IoPath.Combine(_root, "either.mm"), "a body without a registration");
        var rival = new DiagramDefinition(new DiagramOrigin("rival", "mindmap"), "Rival map", ".mm");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root), extraDefinitions: [rival]);

        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.ambiguous-extension", stored.Problem.RuleId);
        Assert.Contains("freeplane/mindmap", stored.Problem.Message);
        Assert.Contains("rival/mindmap", stored.Problem.Message);
    }

    [Fact]
    public async Task ValidateAsync_IgnoresAFileNoTypeClaims()
    {
        File.WriteAllText(IoPath.Combine(_root, "notes.txt"), "just notes");

        var outcome = await Validate(validator: null, new ProjectValidationScope(_root));

        Assert.Empty(outcome.Problems);
        Assert.Equal(0, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_ReportsABodyItCannotRead_AndWalksOn()
    {
        CreatePair("locked", "unreachable");
        CreatePair("open", "reachable");
        var validator = Validator(problems: []);

        using (new FileStream(IoPath.Combine(_root, "locked.mm"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await Validate(validator, new ProjectValidationScope(_root));

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
        CreatePair("a", "first");
        CreatePair("b", "second");
        var validator = Validator(throwing: true);

        var outcome = await Validate(validator, new ProjectValidationScope(_root));

        Assert.Equal(2, outcome.Problems.Count);
        Assert.All(outcome.Problems, stored => Assert.Equal("core.validator-failed", stored.Problem.RuleId));
        Assert.Equal(2, validator.Calls);
    }

    [Fact]
    public async Task ValidateAsync_AbandonsAHangingValidator()
    {
        CreatePair("slow", "the document");
        var validator = Validator(hanging: true);

        var outcome = await Validate(validator, new ProjectValidationScope(_root), timeout: TimeSpan.FromMilliseconds(100));

        var stored = Assert.Single(outcome.Problems);
        Assert.Equal("core.validator-failed", stored.Problem.RuleId);
        Assert.Contains("did not answer", stored.Problem.Message);
    }

    // ---- scopes ------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_FolderScope_TouchesOnlyThatFolder()
    {
        Directory.CreateDirectory(IoPath.Combine(_root, "inside"));
        CreatePair(IoPath.Combine("inside", "in"), "inside the folder");
        CreatePair("out", "outside the folder");
        var validator = Validator(problems: [new DiagramProblem(DiagramProblemSeverity.Error, "Broken.", "mindmap.broken")]);

        var outcome = await Validate(validator, new FolderValidationScope(_root, "inside"));

        var stored = Assert.Single(outcome.Problems);
        Assert.Equal(IoPath.Combine("inside", "in.adp"), stored.RelativePath);
        Assert.Equal(1, validator.Calls);
    }

    [Fact]
    public async Task ValidateAsync_FileScope_TouchesOnlyThatFile()
    {
        CreatePair("one", "first");
        CreatePair("two", "second");
        var validator = Validator(problems: []);

        var outcome = await Validate(validator, new FileValidationScope(_root, "one.adp"));

        Assert.Equal(1, validator.Calls);
        Assert.Equal(1, outcome.FilesConsidered);
    }

    [Fact]
    public async Task ValidateAsync_RefusesAPathThatLeavesTheRoot()
    {
        var elsewhere = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        try
        {
            File.WriteAllText(IoPath.Combine(elsewhere, "outside.adp"), "freeplane/mindmap\n");
            var validator = Validator(problems: []);

            var outcome = await Validate(validator, new FileValidationScope(_root, IoPath.Combine("..", IoPath.GetFileName(elsewhere), "outside.adp")));

            Assert.Empty(outcome.Problems);
            Assert.Equal(0, outcome.FilesConsidered);
            Assert.Equal(1, outcome.Skipped);
            Assert.Equal(0, validator.Calls);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    // ---- the project folder is sacred --------------------------------------------------

    [Fact]
    public async Task ValidateAsync_LeavesTheProjectByteForByteUnchanged()
    {
        CreatePair("flow", "the document");
        File.WriteAllText(IoPath.Combine(_root, "strange.adp"), "vendor/unheard-of\n");
        File.WriteAllText(IoPath.Combine(_root, "notes.txt"), "just notes");
        var before = Snapshot();

        await Validate(Validator(throwing: true), new ProjectValidationScope(_root));

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
        CreatePair("flow", "the document");
        var validator = Validator(problems: []);
        validator.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subject = Subject(validator);

        var first = subject.ValidateAsync(new ProjectValidationScope(_root), TestContext.Current.CancellationToken).AsTask();
        while (validator.Calls == 0)
        {
            await Task.Yield(); // The first run is inside the validator - the second must join it.
        }
        var second = subject.ValidateAsync(new FileValidationScope(_root, "flow.adp"), TestContext.Current.CancellationToken).AsTask();

        validator.Hold.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Equal(1, validator.Calls); // One traversal...
        Assert.Same(outcomes[0], outcomes[1]); // ...two identical answers.
    }

    // ---- plumbing ----------------------------------------------------------------------

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
