using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Rename under nesting, in both directions (adp-file-nesting Requirement 5): a subject carries
/// its registration set, a registration changes only its qualifier, and a refusal moves nothing.
/// </summary>
public class RenameEntryNestingTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;
    private readonly IHistoryStack _history;

    public RenameEntryNestingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _, Mindmap);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string[] Names() =>
        Directory.EnumerateFiles(_root).Select(IoPath.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task RenamingASubject_CarriesEveryRegistration_PreservingQualifiers()
    {
        // Arrange: the shape Requirement 5.1 names - test.dsl-like subject, qualified set.
        var subject = Write("test.mm", "<map/>");
        Write("test.adp", Mindmap.Origin.MimeType + "\n");
        Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        Write("test.second.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");

        // Act.
        var result = await _history.ExecuteAsync(new RenameEntryCommand(subject, "prod.mm"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "prod.adp", "prod.first.adp", "prod.mm", "prod.second.adp" }, Names());

        // The headers moved with their files and now name the renamed subject (Requirement 5.6's
        // failure mode designed out rather than surfaced).
        Assert.Contains("body: prod.mm", await File.ReadAllTextAsync(IoPath.Combine(_root, "prod.first.adp"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenamingASubject_IsUndoneAsASet()
    {
        // Arrange.
        var subject = Write("test.mm", "<map/>");
        Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");

        // Act.
        var renamed = await _history.ExecuteAsync(new RenameEntryCommand(subject, "prod.mm"), TestContext.Current.CancellationToken);
        Assert.True(renamed.IsSuccess, renamed.Error);
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(new[] { "test.first.adp", "test.mm" }, Names());
        Assert.Contains("body: test.mm", await File.ReadAllTextAsync(IoPath.Combine(_root, "test.first.adp"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenamingASubjectIntoACollision_RefusesAndMovesNothing()
    {
        // Arrange: prod.first.adp already exists, so the set cannot land whole (Requirement 5.3, 5.4).
        var subject = Write("test.mm", "<map/>");
        Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        Write("prod.first.adp", Mindmap.Origin.MimeType + "\n");
        var before = Names();

        // Act.
        var result = await _history.ExecuteAsync(new RenameEntryCommand(subject, "prod.mm"), TestContext.Current.CancellationToken);

        // Assert: refused, the conflict named, and the filesystem untouched.
        Assert.False(result.IsSuccess);
        Assert.Contains("prod.first.adp", result.Error);
        Assert.Equal(before, Names());
    }

    [Fact]
    public async Task RenamingARegistration_MayChangeOnlyItsQualifier()
    {
        // Arrange.
        Write("test.mm", "<map/>");
        var registration = Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");

        // Act: a qualifier-only change is fine...
        var allowed = await _history.ExecuteAsync(new RenameEntryCommand(registration, "test.primary.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(allowed.IsSuccess, allowed.Error);
        Assert.Contains("test.primary.adp", Names());
    }

    [Fact]
    public async Task RenamingARegistrationsSubjectPortion_IsRefusedWithTheWayOutNamed()
    {
        // Arrange.
        Write("test.mm", "<map/>");
        var registration = Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        var before = Names();

        // Act: re-pointing the registration at another subject is exactly what 5.2 refuses.
        var refused = await _history.ExecuteAsync(new RenameEntryCommand(registration, "prod.first.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(refused.IsSuccess);
        Assert.Contains("qualifier", refused.Error);
        Assert.Equal(before, Names());
    }

    [Fact]
    public async Task RenamingTheClassicPair_StillWorks_UntilPeersExist()
    {
        // Arrange: the single-registration pair renames exactly as it always has (Requirement 11.1)...
        Write("solo.mm", "<map/>");
        var registration = Write("solo.adp", Mindmap.Origin.MimeType + "\n");

        // Act.
        var classic = await _history.ExecuteAsync(new RenameEntryCommand(registration, "moved.adp"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(classic.IsSuccess, classic.Error);
        Assert.Equal(new[] { "moved.adp", "moved.mm" }, Names());

        // ...but with a qualified peer over the same subject, carrying the subject off would
        // orphan the peer, so the pair rename refuses and points at the subject instead.
        Write("moved.second.adp", Mindmap.Origin.MimeType + "\nbody: moved.mm\n");
        var refused = await _history.ExecuteAsync(new RenameEntryCommand(IoPath.Combine(_root, "moved.adp"), "again.adp"), TestContext.Current.CancellationToken);
        Assert.False(refused.IsSuccess);
        Assert.Contains("moved.mm", refused.Error);
    }
}
