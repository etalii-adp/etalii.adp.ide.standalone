using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Registration;

/// <summary>The blocks of a registration a binding declares, and those it does not (FBL 0.7 §8.1, §8.4).</summary>
public class DeclaredBlocksTests
{
    /// <summary>The binding of the <c>registration-blocks</c> fixture: <c>tags</c> kept, <c>collapsed</c> ordinal by ids outside the history, <c>filters</c> kept.</summary>
    private static readonly FblBinding _tasks = Binding();

    private static FblBinding Binding()
    {
        var fixture = Path.Combine(Repository.Conformance, "etalii.adp", "specifications", "fbl", "fixtures", "registration-blocks", "fixture.json");
        var binding = FblDocumentLoader.ResolveReference("tasks.fbl#tasks", fixture, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return binding;
    }

    [Fact]
    public void TheBindingsBlocksAreRead()
    {
        // Act.
        var blocks = _tasks.Registration.Blocks;

        // Assert.
        Assert.Equal(
            [new RegistrationBlockDeclaration("tags", true, false, true), new RegistrationBlockDeclaration("collapsed", false, true, false), new RegistrationBlockDeclaration("filters", true, false, true)],
            blocks);
    }

    [Fact]
    public void ReadingGoesOnPastAnUndeclaredBlock_KeepsItsBytes_AndReportsItOnceAtInformation()
    {
        // Arrange: notes is declared by no one, tags after it is the binding's.
        var bytes = "example/tasks\r\nnotes:\r\n  first: keep me\r\ntags:\r\n  security: red\r\n"u8.ToArray();

        // Act.
        var registration = OpenRegistration.Open(bytes, _tasks);
        var findings = registration.Findings("plan.adp");

        // Assert: the declared block after it is read.
        var tags = registration.Document.Block("tags");
        Assert.NotNull(tags);
        Assert.Equal("red", Assert.Single(tags.Entries).Value);
        var unknown = Assert.Single(findings, finding => finding.Code == FindingCodes.UnknownBlock);
        Assert.Equal(FindingSeverity.Info, unknown.Severity);
        Assert.Equal(("plan.adp", 2, 1), (unknown.Location!.File, unknown.Location.Line, unknown.Location.Column));

        // Act: a setting written after it.
        var result = registration.Change(new ModelChange.Setting("tags", "core", "default"));

        // Assert: the undeclared block's bytes are kept.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("example/tasks\r\nnotes:\r\n  first: keep me\r\ntags:\r\n  security: red\r\n  core: default\r\n"u8.ToArray(), registration.Bytes);
    }

    [Fact]
    public void AValueHoldingAColonAndASpace_IsRefused()
    {
        // Arrange.
        var bytes = "example/tasks\r\ntags:\r\n  security: red\r\n"u8.ToArray();
        var registration = OpenRegistration.Open(bytes, _tasks);

        // Act.
        var result = registration.Change(new ModelChange.Setting("tags", "legal", "see: policy"));

        // Assert.
        var refused = Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("A setting's value cannot hold ': '.", refused.Reason);
        Assert.Equal(bytes, registration.Bytes);
    }

    [Fact]
    public void ARenameToAKeyTheBlockHas_IsRefused()
    {
        // Arrange.
        var bytes = "example/tasks\r\ntags:\r\n  security: red\r\n  core: default\r\n"u8.ToArray();
        var registration = OpenRegistration.Open(bytes, _tasks);

        // Act.
        var result = registration.Change(new ModelChange.Setting("tags", "core", Rename: "security"));

        // Assert.
        var refused = Assert.IsType<PlanResult.Refused>(result);
        Assert.Contains("'security'", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(bytes, registration.Bytes);
    }

    [Fact]
    public void ARenameIsAReplaceValueOfTheKey()
    {
        // Arrange.
        var registration = OpenRegistration.Open("example/tasks\r\ntags:\r\n  security: red\r\n"u8.ToArray(), _tasks);

        // Act.
        var result = registration.Change(new ModelChange.Setting("tags", "security", Rename: "safety"));

        // Assert.
        var splice = Assert.Single(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        Assert.Equal(new Splice(SpliceOperation.ReplaceValue, 24, 32, "safety"), splice);
        Assert.Equal("example/tasks\r\ntags:\r\n  safety: red\r\n"u8.ToArray(), registration.Bytes);
    }

    [Fact]
    public void RemovingTheLastEntry_RemovesTheBlocksLineWithIt()
    {
        // Arrange.
        var registration = OpenRegistration.Open("example/tasks\r\ntags:\r\n  security: red\r\n"u8.ToArray(), _tasks);

        // Act.
        var result = registration.Change(new ModelChange.Setting("tags", "security", RemoveEntry: true));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("example/tasks\r\n"u8.ToArray(), registration.Bytes);
    }

    [Fact]
    public void ASettingOfABlockTheBindingDoesNotDeclare_IsRefused()
    {
        // Arrange.
        var registration = OpenRegistration.Open("example/tasks\r\n"u8.ToArray(), _tasks);

        // Act.
        var result = registration.Change(new ModelChange.Setting("notes", "first", "keep me"));

        // Assert.
        Assert.IsType<PlanResult.Refused>(result);
        Assert.Equal("example/tasks\r\n"u8.ToArray(), registration.Bytes);
    }
}
