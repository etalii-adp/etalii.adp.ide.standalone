using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// A position written through FBL's registration writer (FBL §8.3) is one undo away, the undo
/// restores the registration byte for byte, and an undo over a registration that changed on disk
/// since is refused with FBL §7.2's sentence (dotnet-dependency-graph-disl-fbl Requirement 5.1).
/// </summary>
public sealed class PlaceInRegistrationCommandTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("place-in-registration");
    private readonly string _root;
    private readonly IHistoryStack _history;

    public PlaceInRegistrationCommandTests()
    {
        _root = _testFolder.Path;
        _history = TestHistory.Create(_root, out _);
    }

    public void Dispose() => _testFolder.Dispose();

    private string WriteAdp(string content)
    {
        var path = IoPath.Combine(_root, "plan.adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task AMove_SplicesItsEntryIn_AndLeavesEveryOtherByteAsItWas()
    {
        // Arrange: a hand-written block out of order, with a number core would rewrite, and prose below.
        var adp = WriteAdp("vendor/type\r\nlayout:\r\n  d: 1.50 2\r\n  a: 3 4\r\nnotes kept\r\n");

        // Act.
        var result = await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "e", 7, 8), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("vendor/type\r\nlayout:\r\n  d: 1.50 2\r\n  a: 3 4\r\n  e: 7 8\r\nnotes kept\r\n", await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFirstMove_AndItsUndo_RestoreTheRegistrationByteForByte()
    {
        // Arrange.
        const string before = "vendor/type\r\nbody: plan.txt\r\n";
        var adp = WriteAdp(before);

        // Act.
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "a", 120, 240), TestContext.Current.CancellationToken);
        var moved = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);
        var undo = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("vendor/type\r\nbody: plan.txt\r\nlayout:\r\n  a: 120 240\r\n", moved);
        Assert.True(undo.IsSuccess, undo.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecondMove_UndoesToTheFirst()
    {
        // Arrange.
        var adp = WriteAdp("vendor/type\nlayout:\n  a: 1 2\n");
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "a", 10, 20), TestContext.Current.CancellationToken);
        var afterFirst = await File.ReadAllBytesAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "a", 300, 400), TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(afterFirst, await File.ReadAllBytesAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUndo_AfterTheRegistrationChangedOnDisk_IsRefusedWithFblsSentence_AndWritesNothing()
    {
        // Arrange.
        var adp = WriteAdp("vendor/type\r\nlayout:\r\n  a: 1 2\r\n");
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "b", 3, 4), TestContext.Current.CancellationToken);
        const string edited = "vendor/type\r\nlayout:\r\n  a: 9 9\r\n  b: 3 4\r\n";
        await File.WriteAllTextAsync(adp, edited, TestContext.Current.CancellationToken);

        // Act.
        var undo = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert: refused, written over nothing, and still there to undo once the file is put back.
        Assert.False(undo.IsSuccess);
        Assert.Equal(SplicedFile.DriftUndo, undo.Error);
        Assert.Equal(edited, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
        Assert.True(_history.CanUndo);
    }

    [Fact]
    public async Task RedoAfterUndo_PlacesTheElementAgain()
    {
        // Arrange.
        var adp = WriteAdp("vendor/type\r\n");
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "a", 5, 6), TestContext.Current.CancellationToken);
        var moved = await File.ReadAllBytesAsync(adp, TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Act.
        var redo = await _history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(redo.IsSuccess, redo.Error);
        Assert.Equal(moved, await File.ReadAllBytesAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMoveToWhereTheElementAlreadyIs_WritesNothing_AndRecordsNothing()
    {
        // Arrange.
        var adp = WriteAdp("vendor/type\r\nlayout:\r\n  a: 1 2\r\n");
        var written = File.GetLastWriteTimeUtc(adp);

        // Act.
        var result = await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "a", 1, 2), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_history.CanUndo);
        Assert.Equal(written, File.GetLastWriteTimeUtc(adp));
    }

    [Fact]
    public async Task AMove_KeepsAnEntryForAnElementItDoesNotKnow()
    {
        // Arrange: the command knows no reading, so no entry is stale to it.
        var adp = WriteAdp("vendor/type\r\nlayout:\r\n  gone: 1 2\r\n");

        // Act.
        await _history.ExecuteAsync(new PlaceInRegistrationCommand(adp, "here", 3, 4), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("vendor/type\r\nlayout:\r\n  gone: 1 2\r\n  here: 3 4\r\n", await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMove_OfARegistrationThatIsGone_IsRefused()
    {
        // Act.
        var result = await _history.ExecuteAsync(new PlaceInRegistrationCommand(IoPath.Combine(_root, "missing.adp"), "a", 1, 2), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("The registration file is no longer there.", result.Error);
    }
}
