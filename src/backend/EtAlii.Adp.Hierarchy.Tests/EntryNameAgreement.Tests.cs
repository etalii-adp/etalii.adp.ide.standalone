using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// One validator of entry names (code-structure T178, behaviour change B-07): a rename refuses exactly
/// the names the dialog refuses while the user types, with the same sentence, because the rules the
/// dialog runs are the ones the rename runs.
/// </summary>
public sealed class EntryNameAgreementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-entry-names-").FullName;
    private readonly RenameEntryCommandHandler _handler = new(new EmptyCatalog(), new FileWriter());

    public void Dispose() => TestFolder.TryDelete(_root);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("sub/new.txt")]
    [InlineData("sub\\new.txt")]
    [InlineData("../escaped.txt")]
    [InlineData("C:name.txt")]
    [InlineData("na*me.txt")]
    [InlineData("na?me.txt")]
    [InlineData("old.txt")]
    [InlineData("taken.txt")]
    [InlineData("Old.txt")]
    [InlineData("new.txt")]
    [InlineData("  new.txt  ")]
    public async Task ARename_RefusesWhatTheDialogRefuses_InTheDialogsWords(string name)
    {
        // Arrange: the entry being renamed, and a name already taken beside it.
        var path = IoPath.Combine(_root, "old.txt");
        await File.WriteAllTextAsync(path, "content", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "taken.txt"), "", TestContext.Current.CancellationToken);
        var dialog = EntryNameRules.Validate(name, _root, currentName: "old.txt");

        // Act.
        var result = await _handler.ExecuteAsync(new RenameEntryCommand(path, name), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(dialog.Valid, result.IsSuccess);
        if (!dialog.Valid)
        {
            Assert.Equal(dialog.Reason, result.Error);
        }
    }

    [Fact]
    public async Task ANameWithSurroundingSpace_IsRenamedToTheNameTheDialogJudged()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "old.txt");
        await File.WriteAllTextAsync(path, "content", TestContext.Current.CancellationToken);

        // Act.
        var result = await _handler.ExecuteAsync(new RenameEntryCommand(path, "  new.txt  "), TestContext.Current.CancellationToken);

        // Assert: the dialog judged "new.txt", so that is the name the entry takes.
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(IoPath.Combine(_root, "new.txt")));
    }
}
