using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Body derivation under the qualified name (adp-file-nesting Requirement 2) - the sharpest
/// technical problem in that spec, because `SiblingPathFor` used to strip one `.adp` and append
/// the extension, deriving `test.first.dsl` from `test.first.adp`.
/// </summary>
public class DiagramFilePairDerivationTests : IDisposable
{
    private static readonly DiagramDefinition _definition =
        new(new DiagramOrigin("c4", "context"), "C4 context", "A test type.", Extension: ".dsl");

    private readonly string _root;
    private readonly IDiagramDefinitionCatalog _catalog = new DiagramDefinitionCatalog { All = [_definition] };

    public DiagramFilePairDerivationTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Nesting", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestFolder.TryDelete(_root);
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
}
