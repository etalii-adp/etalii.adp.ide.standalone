using EtAlii.Adp.Backend;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The command discipline over real files: execute lands the writer's splice through the store,
/// the inverse restores the document byte for byte, and the inverse's redo is the original
/// command (rdf-diagram Requirement 5.5).
/// </summary>
public class RdfCommandsTests : IDisposable
{
    private const string Corpus =
        "@prefix ex: <http://example.org/> .\r\n"
        + "\r\n"
        + "ex:alice ex:knows ex:bob ;\r\n"
        + "    ex:name \"Alice\" . # who else\r\n";

    private readonly string _root;
    private readonly RdfDocumentStore _store = new();
    private readonly string _path;

    public RdfCommandsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "graph.ttl");
        File.WriteAllText(_path, Corpus);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private async Task AssertRoundTrips(ICommand command, Func<ICommand, Task<CommandResult>> execute)
    {
        // Act: execute.
        var result = await execute(command);

        // Assert: it ran and the file changed.
        Assert.True(result.IsSuccess, result.Error);
        var edited = await File.ReadAllTextAsync(_path);
        Assert.NotEqual(Corpus, edited);

        // Act: undo through the reported inverse.
        var restore = Assert.IsType<RestoreRdfDocumentCommand>(result.Inverse);
        var undone = await new RestoreRdfDocumentCommandHandler(_store).ExecuteAsync(restore, TestContext.Current.CancellationToken);

        // Assert: bytes restored exactly - comments, formatting and abbreviations included -
        // and the undo's own inverse is the original command, so redo re-runs the same edit.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path));
        Assert.Same(command, undone.Inverse);

        // Act: redo.
        var redone = await execute(command);

        // Assert: the same edit landed again.
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.Equal(edited, await File.ReadAllTextAsync(_path));
    }

    [Fact]
    public async Task AddTriple_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new AddRdfTripleCommandHandler(_store);
        await AssertRoundTrips(
            new AddRdfTripleCommand(_path, "http://example.org/alice", "http://example.org/knows", "http://example.org/carol"),
            command => handler.ExecuteAsync((AddRdfTripleCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveTriple_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new RemoveRdfTripleCommandHandler(_store);
        await AssertRoundTrips(
            new RemoveRdfTripleCommand(_path, "http://example.org/alice", "http://example.org/knows", "http://example.org/bob"),
            command => handler.ExecuteAsync((RemoveRdfTripleCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveResource_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new RemoveRdfResourceCommandHandler(_store);
        await AssertRoundTrips(
            new RemoveRdfResourceCommand(_path, "http://example.org/bob"),
            command => handler.ExecuteAsync((RemoveRdfResourceCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenameTerm_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new RenameRdfTermCommandHandler(_store);
        await AssertRoundTrips(
            new RenameRdfTermCommand(_path, "http://example.org/bob", "http://example.org/robert"),
            command => handler.ExecuteAsync((RenameRdfTermCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceObjectLiteral_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new ReplaceRdfObjectLiteralCommandHandler(_store);
        await AssertRoundTrips(
            new ReplaceRdfObjectLiteralCommand(
                _path, "http://example.org/alice", "http://example.org/name",
                "Alice", "", "", "Alicia", "en"),
            command => handler.ExecuteAsync((ReplaceRdfObjectLiteralCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddPrefix_ExecuteUndoRedo_RoundTripsByteForByte()
    {
        var handler = new AddRdfPrefixCommandHandler(_store);
        await AssertRoundTrips(
            new AddRdfPrefixCommand(_path, "foaf", "http://xmlns.com/foaf/0.1/"),
            command => handler.ExecuteAsync((AddRdfPrefixCommand)command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARefusedCommand_FailsWithTheSentence_AndTheFileIsUntouched()
    {
        // Arrange.
        var handler = new RemoveRdfTripleCommandHandler(_store);

        // Act.
        var result = await handler.ExecuteAsync(
            new RemoveRdfTripleCommand(_path, "http://example.org/nobody", "http://example.org/knows", "http://example.org/bob"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("nothing to remove", result.Error);
        Assert.Null(result.Inverse);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnparseableFile_RefusesEveryEdit_NamingTheState()
    {
        // Arrange.
        await File.WriteAllTextAsync(_path, "this is ; not turtle @@@\r\n", TestContext.Current.CancellationToken);
        var handler = new AddRdfPrefixCommandHandler(_store);

        // Act.
        var result = await handler.ExecuteAsync(
            new AddRdfPrefixCommand(_path, "ex", "http://example.org/"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("does not parse", result.Error);
    }
}
