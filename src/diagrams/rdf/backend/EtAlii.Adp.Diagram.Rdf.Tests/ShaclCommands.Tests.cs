using EtAlii.Adp.Backend;
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
        Assert.NotEqual(Corpus, File.ReadAllText(_path));

        var restore = Assert.IsType<RestoreRdfDocumentCommand>(result.Inverse);
        var undone = await new RestoreRdfDocumentCommandHandler(_store).ExecuteAsync(restore, TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Corpus, File.ReadAllText(_path));
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
        Assert.Equal(Corpus, File.ReadAllText(_path));
    }
}
