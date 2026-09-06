using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The edit commands over real files (causal-loop-diagram Requirements 4.1-4.6): each lands its
/// writer's splice through the store, each inverse restores the document byte for byte, and each
/// touches only the lines it must.
/// </summary>
public class CausalLoopCommandsTests : IDisposable
{
    private const string Corpus =
        "causal-loop 1\r\n"
        + "\r\n"
        + "# the classic pair\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "\r\n"
        + "link population -> births +\r\n"
        + "link births -> population + # the one that matters\r\n"
        + "\r\n"
        + "loop R1 \"Births beget births\" population births\r\n";

    private readonly string _root;
    private readonly string _path;
    private readonly CausalLoopDocumentStore _store = new();

    public CausalLoopCommandsTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = IoPath.Combine(_root, "feedback.cld");
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

        var restore = Assert.IsType<RestoreCausalLoopDocumentCommand>(result.Inverse);
        var undone = await new RestoreCausalLoopDocumentCommandHandler(_store)
            .ExecuteAsync(restore, TestContext.Current.CancellationToken);

        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path));
        Assert.Same(command, undone.Inverse);
    }

    // ---- every command round-trips ---------------------------------------------------------

    [Fact]
    public Task AddingAVariable_IsOneUndoAway() =>
        AssertRoundTrips(
            new AddVariableCommand(_path, "deaths", "Deaths"),
            command => new AddVariableCommandHandler(_store).ExecuteAsync(
                (AddVariableCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task RenamingAVariable_IsOneUndoAway() =>
        AssertRoundTrips(
            new RenameVariableCommand(_path, "births", "birthRate"),
            command => new RenameVariableCommandHandler(_store).ExecuteAsync(
                (RenameVariableCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task RemovingAVariable_IsOneUndoAway() =>
        AssertRoundTrips(
            new RemoveVariableCommand(_path, "births"),
            command => new RemoveVariableCommandHandler(_store).ExecuteAsync(
                (RemoveVariableCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task SettingALinkPolarity_IsOneUndoAway() =>
        AssertRoundTrips(
            new SetLinkPolarityCommand(_path, "population", "births", CausalLoopPolarity.Negative),
            command => new SetLinkPolarityCommandHandler(_store).ExecuteAsync(
                (SetLinkPolarityCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task SettingAWeight_IsOneUndoAway() =>
        AssertRoundTrips(
            new SetLinkWeightCommand(_path, "population", "births", 2.5),
            command => new SetLinkWeightCommandHandler(_store).ExecuteAsync(
                (SetLinkWeightCommand)command, TestContext.Current.CancellationToken));

    [Fact]
    public Task RemovingALoop_IsOneUndoAway() =>
        AssertRoundTrips(
            new RemoveLoopCommand(_path, "R1"),
            command => new RemoveLoopCommandHandler(_store).ExecuteAsync(
                (RemoveLoopCommand)command, TestContext.Current.CancellationToken));

    // ---- the splice is minimal -------------------------------------------------------------

    /// <summary>
    /// An undo that restores the bytes proves the edit is reversible, not that it was small. A
    /// writer that reserialized the whole document from the model would pass every round-trip
    /// above and still destroy the author's comments, blank lines and ordering.
    /// </summary>
    [Fact]
    public async Task SettingAPolarity_RewritesOneLineAndLeavesEveryOtherByte()
    {
        // Act.
        var result = await new SetLinkPolarityCommandHandler(_store).ExecuteAsync(
            new SetLinkPolarityCommand(_path, "population", "births", CausalLoopPolarity.Negative),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);

        // Assert.
        var before = Corpus.Split("\r\n");
        var after = (await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken)).Split("\r\n");

        Assert.Equal(before.Length, after.Length);
        Assert.Contains("link population -> births -", after);

        // Exactly one line differs, and everything else - the header, the comment, the blanks,
        // the trailing comment on the other link - is byte-identical.
        var changed = before.Where((line, index) => line != after[index]).ToArray();
        Assert.Single(changed);
        Assert.Equal("link population -> births +", changed[0]);
    }

    [Fact]
    public async Task AddingAVariable_AddsOneLineBesideTheOtherVariables()
    {
        // Act.
        await new AddVariableCommandHandler(_store).ExecuteAsync(
            new AddVariableCommand(_path, "deaths", "Deaths"), TestContext.Current.CancellationToken);

        // Assert.
        var after = (await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken)).Split("\r\n");
        Assert.Equal(Corpus.Split("\r\n").Length + 1, after.Length);

        // Beside the other variables rather than at the end of the file: a new statement joins
        // its own kind, so the document stays readable.
        var variables = after.Select((line, index) => (line, index))
            .Where(entry => entry.line.StartsWith("variable ", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToArray();
        Assert.Equal(variables.Length, variables.Last() - variables.First() + 1);

        // And the comment above them did not move.
        Assert.Contains("# the classic pair", after);
    }

    // ---- the two rules that make this writer this writer ------------------------------------

    /// <summary>
    /// Requirement 4.4. A link belongs to the diagram; a loop is a claim about a path through it.
    /// Removing the claim must not remove the causality the author also asserted.
    /// </summary>
    [Fact]
    public async Task RemovingALoop_LeavesItsLinksStanding()
    {
        // Act.
        await new RemoveLoopCommandHandler(_store).ExecuteAsync(
            new RemoveLoopCommand(_path, "R1"), TestContext.Current.CancellationToken);

        // Assert.
        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("loop R1", text, StringComparison.Ordinal);
        Assert.Contains("link population -> births +", text, StringComparison.Ordinal);
        Assert.Contains("link births -> population +", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rename that left the references behind would turn every link through the variable into
    /// a dangling one, and the validator would then report a defect the user did not make.
    /// </summary>
    [Fact]
    public async Task RenamingAVariable_CarriesItsLinksAndLoopsWithIt()
    {
        // Act.
        await new RenameVariableCommandHandler(_store).ExecuteAsync(
            new RenameVariableCommand(_path, "births", "birthRate"), TestContext.Current.CancellationToken);

        // Assert.
        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.Contains("variable birthRate \"Births\"", text, StringComparison.Ordinal);
        Assert.Contains("link population -> birthRate +", text, StringComparison.Ordinal);
        Assert.Contains("link birthRate -> population +", text, StringComparison.Ordinal);
        Assert.Contains("loop R1 \"Births beget births\" population birthRate", text, StringComparison.Ordinal);

        // The rename moved identifiers and left prose alone: the loop is still named "Births
        // beget births", because that is a sentence about the world and not a reference.
        Assert.Contains("\"Births beget births\"", text, StringComparison.Ordinal);

        // No identifier anywhere still refers to the old name, and nothing dangles.
        var model = CausalLoopParser.Parse(CausalLoopDocument.Parse(text)).Model;
        Assert.DoesNotContain(model.Variables, variable => variable.Id == "births");
        Assert.DoesNotContain(model.Links, link => link.From == "births" || link.To == "births");
        Assert.DoesNotContain(model.Loops, loop => loop.Variables.Contains("births", StringComparer.Ordinal));
        Assert.All(model.Links, link => Assert.True(model.Declares(link.From) && model.Declares(link.To)));
    }

    [Fact]
    public async Task RemovingAVariable_TakesTheLinksAndLoopsThatNamedIt_AndSaysHowMany()
    {
        // Arrange.
        var model = _store.GetOrLoad(_path).Model;

        // Act.
        var count = CausalLoopWriter.CountVariableRemoval(model, "births");
        await new RemoveVariableCommandHandler(_store).ExecuteAsync(
            new RemoveVariableCommand(_path, "births"), TestContext.Current.CancellationToken);

        // Assert.
        // The variable, two links and one loop: stated before anything runs, so a confirmation
        // can say what it is about to take.
        Assert.Equal(4, count);

        var text = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("births", text, StringComparison.Ordinal);
        Assert.Contains("variable population", text, StringComparison.Ordinal);
    }

    // ---- refusals ---------------------------------------------------------------------------

    [Fact]
    public async Task ARefusedEdit_FailsWithItsSentence_AndLeavesTheFileUntouched()
    {
        // Act.
        var result = await new SetLinkPolarityCommandHandler(_store).ExecuteAsync(
            new SetLinkPolarityCommand(_path, "population", "nowhere", CausalLoopPolarity.Negative),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(CausalLoopWriter.NoSuchLink, result.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ALinkToAnUndeclaredVariable_IsRefusedRatherThanWritten()
    {
        // Act.
        var result = await new AddLinkCommandHandler(_store).ExecuteAsync(
            new AddLinkCommand(_path, "population", "nowhere", CausalLoopPolarity.Positive),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(CausalLoopWriter.NoSuchVariable, result.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANameTheFormatCannotRoundTrip_IsRefused()
    {
        // Act.
        // A statement is read as words on one line, so a name with a space in it would come
        // back as two names. Refused rather than written and misread on the next open.
        var result = await new AddVariableCommandHandler(_store).ExecuteAsync(
            new AddVariableCommand(_path, "two words", "Label"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(CausalLoopWriter.UnusableName, result.Error);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }

    // ---- a link claims the loops it closes -------------------------------------------------

    /// <summary>Writes a fresh document under the test root and returns its path.</summary>
    private string WriteFresh(string body)
    {
        var path = IoPath.Combine(_root, $"{Guid.NewGuid():N}.cld");
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public async Task AddingALinkThatClosesATwoVariableLoop_ClaimsThatLoop_AndUndoesTogether()
    {
        // Two variables, one link, no loop: the reverse link closes the loop, and stating it
        // should claim that loop in the same undoable edit.
        var path = WriteFresh("causal-loop 1\r\n\r\nvariable a\r\nvariable b\r\nlink a -> b +\r\n");

        // Act.
        var result = await new AddLinkCommandHandler(_store).ExecuteAsync(
            new AddLinkCommand(path, "b", "a", CausalLoopPolarity.Positive), TestContext.Current.CancellationToken);

        // Assert. A loop is claimed, named R for the reinforcing polarity two positive links give.
        Assert.True(result.IsSuccess, result.Error);
        var loop = Assert.Single(_store.GetOrLoad(path).Model.Loops);
        Assert.StartsWith("R", loop.Identifier, StringComparison.Ordinal);
        Assert.Equal(new[] { "a", "b" }, loop.Variables.OrderBy(variable => variable, StringComparer.Ordinal));

        // The link and its loop are one edit: undo removes both.
        var restore = Assert.IsType<RestoreCausalLoopDocumentCommand>(result.Inverse);
        await new RestoreCausalLoopDocumentCommandHandler(_store).ExecuteAsync(restore, TestContext.Current.CancellationToken);
        Assert.Empty(_store.GetOrLoad(path).Model.Loops);
        Assert.Single(_store.GetOrLoad(path).Model.Links);
    }

    [Fact]
    public async Task AddingALinkThatClosesNoLoop_ClaimsNothing()
    {
        var path = WriteFresh("causal-loop 1\r\n\r\nvariable a\r\nvariable b\r\nvariable c\r\nlink a -> b +\r\n");

        // Act. b -> c extends the chain without closing a cycle.
        var result = await new AddLinkCommandHandler(_store).ExecuteAsync(
            new AddLinkCommand(path, "b", "c", CausalLoopPolarity.Positive), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_store.GetOrLoad(path).Model.Loops);
    }

    [Fact]
    public async Task AddingALinkThatClosesANewLoop_LeavesTheExistingClaimAlone()
    {
        // a<->b already claimed as R1; adding c -> a closes a longer loop a -> b -> c -> a.
        var path = WriteFresh(
            "causal-loop 1\r\n\r\nvariable a\r\nvariable b\r\nvariable c\r\n"
            + "link a -> b +\r\nlink b -> a +\r\nlink b -> c +\r\nloop R1 \"\" a b\r\n");

        // Act.
        var result = await new AddLinkCommandHandler(_store).ExecuteAsync(
            new AddLinkCommand(path, "c", "a", CausalLoopPolarity.Positive), TestContext.Current.CancellationToken);

        // Assert. R1 untouched; exactly one new loop, over the three-variable cycle.
        Assert.True(result.IsSuccess, result.Error);
        var loops = _store.GetOrLoad(path).Model.Loops;
        Assert.Equal(2, loops.Count);
        Assert.Contains(loops, loop => loop.Identifier == "R1");
        Assert.Contains(loops, loop => loop.Identifier != "R1" && loop.Variables.Count == 3);
    }

    [Fact]
    public async Task ALoopThroughAnUndeclaredVariable_IsRefusedNamingIt()
    {
        // Act.
        var result = await new AddLoopCommandHandler(_store).ExecuteAsync(
            new AddLoopCommand(_path, "B1", "wishful", ["population", "nowhere"]),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("'nowhere'", result.Error, StringComparison.Ordinal);
        Assert.Equal(Corpus, await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken));
    }
}
