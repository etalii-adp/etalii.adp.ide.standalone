using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Body derivation under the qualified name (adp-file-nesting Requirement 2) - the sharpest
/// technical problem in that spec, because `SiblingPathFor` used to strip one `.adp` and append
/// the extension, deriving `test.first.dsl` from `test.first.adp`.
/// </summary>
public class DiagramFilePairDerivationTests : IDisposable
{
    private static readonly DiagramDefinition _definition =
        new(new ToolOrigin("c4", "context"), "C4 context", "A test type.", Extension: ".dsl");

    private readonly TestFolder _testFolder = TestFolder.Create("pair-derivation");
    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new DiagramDefinitionCatalog { All = [_definition] };

    public DiagramFilePairDerivationTests()
    {
        _root = _testFolder.Path;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _testFolder.Dispose();
    }

    private string Write(string name, string content = "c4/context")
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AnUnqualifiedRegistration_DerivesItsBase_ExactlyAsBefore()
    {
        // Arrange.
        // Requirement 11.1: every existing project uses this form and must resolve as it did.
        var adp = Write("test.adp");
        Write("test.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "test.dsl"), body.Value.Path);
        Assert.True(body.Value.IsOwned);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void AQualifiedRegistration_DerivesTheSubject_NotItsOwnFullName()
    {
        // Arrange.
        // The defect this task exists for: the old derivation produced `test.first.dsl`.
        var adp = Write("test.first.adp");
        Write("test.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "test.dsl"), body.Value.Path);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void SeveralQualifiedRegistrations_AllDeriveTheOneSubject()
    {
        // Arrange.
        var first = Write("test.first.adp");
        var second = Write("test.second.adp");
        Write("test.dsl", "workspace {}");

        // Act.
        var firstBody = DiagramFilePair.BodyOf(first, _catalog, projectRoot: null);
        var secondBody = DiagramFilePair.BodyOf(second, _catalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "test.dsl"), firstBody!.Value.Path);
        Assert.Equal(IoPath.Combine(_root, "test.dsl"), secondBody!.Value.Path);
    }

    [Fact]
    public void TheAmbiguity_WhenOnlyTheLongerBodyExists_ResolvesToIt()
    {
        // Arrange.
        // Requirement 2.2, direction one: a subject genuinely named `my.config`.
        var adp = Write("my.config.adp");
        Write("my.config.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "my.config.dsl"), body!.Value.Path);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void TheAmbiguity_WhenOnlyTheShorterBodyExists_ResolvesToIt()
    {
        // Arrange.
        // Requirement 2.2, direction two: the subject `my` with the qualifier `config`.
        var adp = Write("my.config.adp");
        Write("my.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "my.dsl"), body!.Value.Path);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void TheAmbiguity_WhenBothExist_TakesTheLongerAndReportsTheOther()
    {
        // Arrange.
        // The genuinely ambiguous case. The name cannot say which was meant, so the longer wins
        // and the other is carried out for reporting rather than discarded.
        var adp = Write("my.config.adp");
        Write("my.config.dsl", "workspace {}");
        Write("my.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert, step by step.
        Assert.Equal(IoPath.Combine(_root, "my.config.dsl"), body!.Value.Path);
        Assert.Equal(IoPath.Combine(_root, "my.dsl"), body.Value.AmbiguousWith);
    }

    [Fact]
    public void TheAmbiguity_WhenNeitherExists_TakesTheSubjectReading()
    {
        // Arrange.
        // Requirement 2.1 is the default: a qualified name means a subject and a qualifier.
        var adp = Write("my.config.adp");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "my.dsl"), body!.Value.Path);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void AFolderScopedRegistration_DerivesNoBodyRatherThanABarePath()
    {
        // Arrange.
        // Requirement 4.4: `StripExtension(".adp")` yields an empty base name, and deriving from
        // it would produce a path that is just the extension. Excluded rather than produced.
        var adp = Write(".adp");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, projectRoot: null);

        // Assert.
        Assert.Null(body);
    }

    [Fact]
    public void ABodyHeader_StillWinsOverEveryDerivationRule()
    {
        // Arrange.
        // Requirement 2.3: the header is the escape hatch and stays authoritative. This is also
        // what confines the existence-dependent rule above to hand-authored names, since a
        // qualified registration ADP writes carries a header.
        var adp = Write("test.first.adp", "c4/context\nbody: shared/model.dsl");
        Directory.CreateDirectory(IoPath.Combine(_root, "shared"));
        Write(IoPath.Combine("shared", "model.dsl"), "workspace {}");
        Write("test.dsl", "workspace {}");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert, step by step.
        Assert.Equal(IoPath.Combine(_root, "shared", "model.dsl"), body!.Value.Path);
        Assert.False(body.Value.IsOwned);
    }

    [Fact]
    public void ABodyHeaderPointingOutsideTheProject_IsStillRefused()
    {
        // Arrange.
        // Requirement 2.5: the path-escape guard is preserved unchanged. A header is
        // user-editable text, and following it anywhere on disk would turn a registration into a
        // way to read arbitrary files through the backend.
        var adp = Write("test.first.adp", "c4/context\nbody: ../../../etc/passwd");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert.
        Assert.Null(body);
    }

    [Fact]
    public void ABodyHeaderReachingOutThroughALink_IsRefused()
    {
        // Arrange.
        // The header names a path inside the project, but a link on the way leads out of it
        // (spec 025, D6): reading it would read a file that is not the project's, exactly as a
        // `..` would.
        using var elsewhereFolder = TestFolder.Create("pair-derivation-elsewhere");
        var elsewhere = elsewhereFolder.Path;
        File.WriteAllText(IoPath.Combine(elsewhere, "model.dsl"), "workspace {}");
        try
        {
            Directory.CreateSymbolicLink(IoPath.Combine(_root, "escape"), elsewhere);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && OperatingSystem.IsWindows())
        {
            Assert.Skip($"This process may not create a symbolic link: {exception.Message}");
        }
        var adp = Write("test.first.adp", "c4/context\nbody: escape/model.dsl");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _catalog, _root);

        // Assert.
        Assert.Null(body);
    }

    // ---- a type with two serializations ------------------------------------------------
    //
    // A header-less registration derives its body from the type's primary extension, and from the
    // alternate when no file of the primary is there (FBL 8.2's "the first that exists, in the order of
    // claims.extensions"; dotnet-dependency-graph-disl-fbl task 9, ruled 2026-10-11). The names are
    // neutral: core names no tool.

    private static readonly DiagramDefinition _twoSerializations =
        new(new ToolOrigin("test", "two-serializations"), "Two serializations", "A test type.", Extension: ".new", SharedExtension: true, AlternateExtension: ".old");

    private readonly IDiagramDefinitionCatalog _twoCatalog = new DiagramDefinitionCatalog { All = [_twoSerializations] };

    [Fact]
    public void AHeaderlessRegistration_BesideOnlyTheAlternate_DerivesTheAlternate()
    {
        // Arrange.
        var adp = Write("solution.adp", "test/two-serializations");
        Write("solution.old", "old");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _twoCatalog, projectRoot: null);

        // Assert: opened, and owned as an unqualified registration's sibling always is.
        Assert.NotNull(body);
        Assert.Equal(IoPath.Combine(_root, "solution.old"), body.Value.Path);
        Assert.True(body.Value.IsOwned);
        Assert.Equal(IoPath.Combine(_root, "solution.old"), DiagramFilePair.SiblingOf(adp, _twoCatalog));
    }

    [Fact]
    public void AHeaderlessRegistration_BesideBothSerializations_DerivesThePrimary()
    {
        // Arrange.
        var adp = Write("solution.adp", "test/two-serializations");
        Write("solution.old", "old");
        Write("solution.new", "new");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _twoCatalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "solution.new"), body!.Value.Path);
    }

    [Fact]
    public void AHeaderlessRegistration_BesideNeither_DerivesThePrimary()
    {
        // Arrange: the body a later Add would create, so a missing body is reported under the primary's name.
        var adp = Write("solution.adp", "test/two-serializations");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _twoCatalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "solution.new"), body!.Value.Path);
    }

    [Fact]
    public void AQualifiedRegistration_BesideOnlyTheAlternateOfItsSubject_DerivesThatAlternate()
    {
        // Arrange.
        var adp = Write("solution.first.adp", "test/two-serializations");
        Write("solution.old", "old");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _twoCatalog, projectRoot: null);

        // Assert: shared, as a qualified reading's subject always is.
        Assert.Equal(IoPath.Combine(_root, "solution.old"), body!.Value.Path);
        Assert.False(body.Value.IsOwned);
        Assert.Null(body.Value.AmbiguousWith);
    }

    [Fact]
    public void AQualifiedRegistration_WhoseWholeNameHasOnlyTheAlternate_DerivesTheWholeName()
    {
        // Arrange: the longer reading exists in the alternate only, the shorter in the primary.
        var adp = Write("my.config.adp", "test/two-serializations");
        Write("my.config.old", "old");
        Write("my.new", "new");

        // Act.
        var body = DiagramFilePair.BodyOf(adp, _twoCatalog, projectRoot: null);

        // Assert.
        Assert.Equal(IoPath.Combine(_root, "my.config.old"), body!.Value.Path);
        Assert.Equal(IoPath.Combine(_root, "my.new"), body.Value.AmbiguousWith);
        Assert.True(body.Value.IsOwned);
    }
}
