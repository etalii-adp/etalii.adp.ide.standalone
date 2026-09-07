using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// This reading's commands over real files: each lands its writer's splice through the family
/// store, and each inverse restores the document byte for byte - including the removal that
/// sweeps a blank-node subtree, which is the one that most needs saying (shacl-diagram
/// Requirement 5.1).
/// </summary>
public class ShaclCommandsTests : IDisposable
{
    private const string Corpus =
        "@prefix sh: <http://www.w3.org/ns/shacl#> .\r\n"
        + "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "ex:PersonShape a sh:NodeShape ;\r\n"
        + "    sh:targetClass ex:Person ;\r\n"
        + "    sh:property [ sh:path ex:name ; sh:minCount 1 ] . # the one that matters\r\n";

    private const string Ex = "http://example.org/";

    private readonly string _root;
    private readonly RdfDocumentStore _store = new();
    private readonly string _path;

    public ShaclCommandsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "shapes.ttl");
        File.WriteAllText(_path, Corpus);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private async Task AssertRoundTrips(ICommand command, Func<ICommand, Task<CommandResult>> execute)
    {
        var result = await execute(command);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(Corpus, await File.ReadAllTextAsync(_path));

        var restore = Assert.IsType<RestoreRdfDocumentCommand>(result.Inverse);
        var undone = await new RestoreRdfDocumentCommandHandler(_store).ExecuteAsync(restore, TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path));
        Assert.Same(command, undone.Inverse);
    }

    [Fact]
    public Task AddingAPropertyRow_IsOneUndoAway() =>
        AssertRoundTrips(
            new AddShaclPropertyRowCommand(_path, Ex + "PersonShape", Ex + "age", MinCount: 0, MaxCount: 1),
            command => new AddShaclPropertyRowCommandHandler(_store)
                .ExecuteAsync((AddShaclPropertyRowCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task AddingATarget_IsOneUndoAway() =>
        AssertRoundTrips(
            new AddShaclTargetCommand(_path, Ex + "PersonShape", ShaclVocabulary.TargetNode, Ex + "alice"),
            command => new AddShaclTargetCommandHandler(_store)
                .ExecuteAsync((AddShaclTargetCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task RemovingATarget_IsOneUndoAway() =>
        AssertRoundTrips(
            new RemoveShaclTargetCommand(_path, Ex + "PersonShape", ShaclVocabulary.TargetClass, Ex + "Person"),
            command => new RemoveShaclTargetCommandHandler(_store)
                .ExecuteAsync((RemoveShaclTargetCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task CreatingANodeShape_IsOneUndoAway() =>
        AssertRoundTrips(
            new CreateShaclNodeShapeCommand(_path, Ex + "AddressShape"),
            command => new CreateShaclNodeShapeCommandHandler(_store)
                .ExecuteAsync((CreateShaclNodeShapeCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task DeactivatingAShape_IsOneUndoAway() =>
        AssertRoundTrips(
            new SetShaclDeactivatedCommand(_path, Ex + "PersonShape", Deactivated: true),
            command => new SetShaclDeactivatedCommandHandler(_store)
                .ExecuteAsync((SetShaclDeactivatedCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task RemovingAShapeWithItsSubtree_IsOneUndoAway() =>
        // The one that matters most: the edit sweeps a blank node, and the whole-document
        // snapshot inverse is what makes that reversible at all.
        AssertRoundTrips(
            new RemoveShaclShapeCommand(_path, Ex + "PersonShape"),
            command => new RemoveShaclShapeCommandHandler(_store)
                .ExecuteAsync((RemoveShaclShapeCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ARefusedEdit_FailsWithItsSentence_AndLeavesTheFileUntouched()
    {
        var result = await new AddShaclPropertyRowCommandHandler(_store).ExecuteAsync(
            new AddShaclPropertyRowCommand(_path, Ex + "NoSuchShape", Ex + "p"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ShaclRefusals.NoSuchShape, result.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The other half of the splice discipline, which the round-trip above does not test: an
    /// undo that restores the bytes proves the edit is reversible, not that it was *small*. A
    /// writer that reserialized the whole document from the model would pass every round-trip
    /// here and still destroy the file's formatting, its comment and its author's line breaks.
    /// So each edit is checked line by line: every line the file had, it still has - bar the one
    /// the splice legitimately extends - and the count grows by what was actually added.
    /// </summary>
    [Fact]
    public Task AddingATarget_TouchesOnlyTheLinesItMust() =>
        AssertMinimalDiff(
            new AddShaclTargetCommandHandler(_store).ExecuteAsync(
                new AddShaclTargetCommand(_path, Ex + "PersonShape", ShaclVocabulary.TargetNode, Ex + "alice"),
                TestContext.Current.CancellationToken),
            "sh:targetNode");

    [Fact]
    public Task AddingAPropertyRow_TouchesOnlyTheLinesItMust() =>
        AssertMinimalDiff(
            new AddShaclPropertyRowCommandHandler(_store).ExecuteAsync(
                new AddShaclPropertyRowCommand(_path, Ex + "PersonShape", Ex + "age", MinCount: 0, MaxCount: 1),
                TestContext.Current.CancellationToken),
            "sh:path ex:age");

    [Fact]
    public Task Deactivating_TouchesOnlyTheLinesItMust() =>
        AssertMinimalDiff(
            new SetShaclDeactivatedCommandHandler(_store).ExecuteAsync(
                new SetShaclDeactivatedCommand(_path, Ex + "PersonShape", Deactivated: true),
                TestContext.Current.CancellationToken),
            "sh:deactivated");

    private async Task AssertMinimalDiff(Task<CommandResult> execution, string expected)
    {
        var result = await execution;
        Assert.True(result.IsSuccess, result.Error);

        var text = await File.ReadAllTextAsync(_path);
        var before = Corpus.Split("\r\n");
        var after = text.Split("\r\n");

        // Exactly one line more than the file had: each of these edits states one new triple.
        Assert.Equal(before.Length + 1, after.Length);
        Assert.Contains(expected, text, StringComparison.Ordinal);

        // The comment is the canary. Nothing about these edits concerns it, so a writer that
        // moved or dropped it is reserializing the document rather than splicing into it.
        Assert.Contains(after, line => line.Contains("# the one that matters", StringComparison.Ordinal));

        // Exactly one of the file's own lines may change, and only past its terminator: a new
        // pair is appended to the end of the statement, which turns that line's '.' into a ';'.
        // Everything else - prefixes, indentation, the blank line, the trailing comment - has to
        // come through untouched, which is what separates a splice from a reserialization.
        var missing = before.Except(after, StringComparer.Ordinal).ToArray();
        var changed = Assert.Single(missing);

        var terminator = changed.LastIndexOf(" .", StringComparison.Ordinal);
        Assert.True(terminator > 0, $"the changed line is not a statement end: {changed}");
        Assert.Contains(after, line => line.StartsWith(changed[..terminator], StringComparison.Ordinal));
    }
}
