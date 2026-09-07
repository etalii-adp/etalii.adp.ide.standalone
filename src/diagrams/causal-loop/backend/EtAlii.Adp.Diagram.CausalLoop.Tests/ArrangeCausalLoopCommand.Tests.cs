using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// Persisting the arrangement (causal-loop-diagram Requirement 6.8): the result of the
/// self-organizing layout is stored as authored positions through the existing mechanism, so it
/// is one undo away like every other edit and the body is never touched.
/// </summary>
public class ArrangeCausalLoopCommandTests : IDisposable
{
    private const string Body =
        "causal-loop 1\r\n"
        + "\r\n"
        + "# the classic pair\r\n"
        + "variable population \"Population\"\r\n"
        + "variable births \"Births\"\r\n"
        + "variable crowding \"Crowding\"\r\n"
        + "\r\n"
        + "link population -> births +\r\n"
        + "link births -> population +\r\n"
        + "link population -> crowding +\r\n"
        + "link crowding -> population -\r\n";

    /// <summary>
    /// A registration with prose above the layout block and one authored position already in it,
    /// so both halves of "everything above the block survives byte for byte" are testable.
    /// </summary>
    private const string Registration =
        "origin: systems/causal-loop-diagram\r\n"
        + "body: feedback.cld\r\n"
        + "\r\n"
        + "# an author's note that must survive\r\n"
        + "title: Population feedback\r\n"
        + "\r\n"
        + "layout:\r\n"
        + "  variable:population: 10 20\r\n";

    private readonly string _root;
    private readonly string _bodyPath;
    private readonly string _adpPath;
    private readonly CausalLoopDocumentStore _store = new();

    public ArrangeCausalLoopCommandTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _bodyPath = IoPath.Combine(_root, "feedback.cld");
        _adpPath = IoPath.Combine(_root, "feedback.adp");
        File.WriteAllText(_bodyPath, Body);
        File.WriteAllText(_adpPath, Registration);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
        GC.SuppressFinalize(this);
    }

    private Task<CommandResult> Arrange() =>
        new ArrangeCausalLoopCommandHandler(_store).ExecuteAsync(
            new ArrangeCausalLoopCommand(_adpPath, _bodyPath), TestContext.Current.CancellationToken);

    // ---- what it writes, and where -------------------------------------------------------------

    [Fact]
    public async Task TheArrangement_IsStoredAsAuthoredPositions()
    {
        // Act.
        var result = await Arrange();

        // Assert.
        Assert.True(result.IsSuccess, result.Error);

        var stored = RegistrationLayout.Read(_adpPath);
        Assert.Equal(3, stored.Count);
        Assert.Contains("variable:population", stored.Keys);
        Assert.Contains("variable:births", stored.Keys);
        Assert.Contains("variable:crowding", stored.Keys);
    }

    /// <summary>
    /// The positions the session reads back must be the ones the layout computed. Stored
    /// positions are centres — the render subtracts the half-extents again — so a command that
    /// wrote corners would draw every variable half a box off, in a way no test of the layout
    /// itself would notice.
    /// </summary>
    [Fact]
    public async Task ThePositionsStored_AreTheCentresTheLayoutComputed()
    {
        // Arrange.
        var expected = SelfOrganizingLayout.Compute(_store.GetOrLoad(_bodyPath).Model).Boxes;

        // Act.
        await Arrange();

        // Assert.
        var stored = RegistrationLayout.Read(_adpPath);
        Assert.NotEmpty(expected);
        foreach (var (id, box) in expected)
        {
            var position = stored[$"variable:{id}"];
            Assert.Equal(box.CenterX, position.X, 3);
            Assert.Equal(box.CenterY, position.Y, 3);
        }
    }

    /// <summary>
    /// An arrangement is an opinion about where things are drawn, not a change to what the
    /// document says. A `.cld` reviewed in a pull request shows no diff at all.
    /// </summary>
    [Fact]
    public async Task TheBody_IsNotTouched()
    {
        // Act.
        await Arrange();

        // Assert.
        Assert.Equal(Body, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EverythingAboveTheLayoutBlock_Survives()
    {
        // Act.
        await Arrange();

        // Assert.
        var text = await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken);
        Assert.Contains("# an author's note that must survive", text, StringComparison.Ordinal);
        Assert.Contains("title: Population feedback", text, StringComparison.Ordinal);
        Assert.Contains("origin: systems/causal-loop-diagram", text, StringComparison.Ordinal);
    }

    // ---- one undo, not one per variable --------------------------------------------------------

    /// <summary>
    /// Requirement 6.8. Dispatching one <c>SetRegistrationLayoutCommand</c> per box would make
    /// undoing a forty-variable arrangement forty undos, which is not what "one undo away like
    /// every other edit" means — so the inverse is a single restore, and it is byte-exact.
    /// </summary>
    [Fact]
    public async Task OneUndo_RestoresTheRegistrationByteForByte()
    {
        // Act.
        var result = await Arrange();
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEqual(Registration, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));

        var restore = Assert.IsType<RestoreCausalLoopRegistrationCommand>(result.Inverse);
        var undone = await new RestoreCausalLoopRegistrationCommandHandler()
            .ExecuteAsync(restore, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(Registration, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));

        // Including the position that was already authored: an undo that dropped it would be
        // losing an edit the arrangement never made.
        Assert.Equal(new RegistrationPosition(10, 20), RegistrationLayout.Read(_adpPath)["variable:population"]);

        // And redoing the undo arranges again.
        Assert.Same(restore.Redo, undone.Inverse);
    }

    /// <summary>
    /// The same document arranged twice writes the same registration, because the layout is a
    /// function of the document and the entries are written in the document's own order.
    /// </summary>
    [Fact]
    public async Task ArrangingTwice_WritesTheSameRegistration()
    {
        // Act.
        await Arrange();
        var first = await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(_adpPath, Registration, TestContext.Current.CancellationToken);
        await Arrange();

        // Assert.
        Assert.Equal(first, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));
    }

    // ---- refusals ------------------------------------------------------------------------------

    /// <summary>
    /// The layout's own sentence, passed through rather than reworded: it already names the size,
    /// and a command that rewrote it would make the same failure read two different ways
    /// depending on where a user met it.
    /// </summary>
    [Fact]
    public async Task ADiagramTheLayoutRefuses_IsRefusedInTheLayoutsOwnWords_AndWritesNothing()
    {
        // Arrange.
        // Past the drawn-element budget, which is the refusal reachable without a fixture that
        // takes the separation pass beyond its rounds.
        var text = new System.Text.StringBuilder("causal-loop 1\r\n");
        for (var index = 0; index < 1200; index++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"variable v{index} \"V{index}\"\r\n");
        }

        await File.WriteAllTextAsync(_bodyPath, text.ToString(), TestContext.Current.CancellationToken);
        _store.Reload(_bodyPath);

        var expected = SelfOrganizingLayout.Compute(_store.GetOrLoad(_bodyPath).Model);

        // Act.
        var result = await Arrange();

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(expected.Refusal, result.Error);
        Assert.Contains("1200 variables", result.Error, StringComparison.Ordinal);

        // Nothing written: the diagram is left as it was rather than half-arranged.
        Assert.Equal(Registration, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADiagramWithNoVariables_IsRefusedRatherThanArrangedToNothing()
    {
        // Arrange.
        await File.WriteAllTextAsync(_bodyPath, "causal-loop 1\r\n", TestContext.Current.CancellationToken);
        _store.Reload(_bodyPath);

        // Act.
        var result = await Arrange();

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no variables", result.Error, StringComparison.Ordinal);
        Assert.Equal(Registration, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingRegistration_IsRefusedRatherThanCreated()
    {
        // Arrange.
        File.Delete(_adpPath);

        // Act.
        var result = await Arrange();

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("no longer there", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(_adpPath));
    }

    [Fact]
    public async Task AnUnreadableBody_IsRefusedWithoutTouchingTheRegistration()
    {
        // Arrange.
        await File.WriteAllTextAsync(_bodyPath, "this is not a causal loop diagram at all\r\n", TestContext.Current.CancellationToken);
        _store.Reload(_bodyPath);

        // Act.
        var result = await Arrange();

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(Registration, await File.ReadAllTextAsync(_adpPath, TestContext.Current.CancellationToken));
    }
}
