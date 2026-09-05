using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A reposition is one undo away like every other edit, and undo returns the registration
/// file byte for byte (databricks-diagrams Requirement 7.6).
/// </summary>
public class SetRegistrationLayoutCommandTests : IDisposable
{
    private readonly string _root;
    private readonly IHistoryStack _history;

    public SetRegistrationLayoutCommandTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string WriteAdp(string content)
    {
        var path = IoPath.Combine(_root, "plan.adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task AFirstMove_StoresThePosition_AndUndoRestoresTheFileByteForByte()
    {
        // Arrange.
        var before = "databricks/job\r\nbody: job.yml\r\n";
        var adp = WriteAdp(before);

        // Act.
        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(adp, "ingest", 120, 240), TestContext.Current.CancellationToken);
        var afterMove = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Contains("layout:\r\n  ingest: 120 240\r\n", afterMove, StringComparison.Ordinal);
        // The element had no entry before, so undo removes entry AND block: bytes identical.
        Assert.Equal(before, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASecondMove_UndoesToTheFirstPosition_NotToNothing()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        await _history.ExecuteAsync(new SetRegistrationLayoutCommand(adp, "ingest", 10, 20), TestContext.Current.CancellationToken);
        var afterFirst = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await _history.ExecuteAsync(new SetRegistrationLayoutCommand(adp, "ingest", 300, 400), TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        // The inverse restores the PRIOR entry, not the entry's absence.
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
        Assert.Equal(new RegistrationPosition(10, 20), RegistrationLayout.Read(adp)["ingest"]);
    }

    [Fact]
    public async Task RedoAfterUndo_ReappliesTheMove()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        await _history.ExecuteAsync(new SetRegistrationLayoutCommand(adp, "ingest", 5, 6), TestContext.Current.CancellationToken);
        var afterMove = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);

        // Act.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        await _history.RedoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(afterMove, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMoveOnAVanishedRegistration_IsRefusedWithTheReason()
    {
        // Arrange & act.
        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(IoPath.Combine(_root, "gone.adp"), "x", 1, 2), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no longer there", result.Error, StringComparison.Ordinal);
    }
}
