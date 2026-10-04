using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>
/// "Arrange diagram" forgets every dragged position so the tidy tree is drawn again, leaves the
/// Markdown alone, and one undo puts the registration back byte for byte.
/// </summary>
public sealed class AbmArrangementTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AbmArrangementTests", Guid.NewGuid().ToString("N"));
    private readonly AbmTestDispatcher _dispatcher = new(new AbmDocumentStore());

    public AbmArrangementTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(AbmExamples.BodyOf("bug-fixer"), Body);
        File.Copy(Path.ChangeExtension(AbmExamples.BodyOf("bug-fixer"), ".adp"), Registration);
    }

    private string Body => Path.Combine(_folder, "fixer.md");

    private string Registration => Path.Combine(_folder, "fixer.adp");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    [Fact]
    public async Task Arranging_ForgetsEveryDraggedPosition_AndItsUndoAndRedoAreExact()
    {
        // Arrange: two nodes dragged away from the tidy tree.
        await Dispatch(new SetRegistrationLayoutCommand(Registration, "1", 900, 40));
        await Dispatch(new SetRegistrationLayoutCommand(Registration, "1.1", -300, 500));
        var dragged = await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken);
        var markdown = await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken);
        Assert.Equal(2, RegistrationLayout.Read(Registration).Count);

        // Act.
        var result = await _dispatcher.DispatchAsync(new ArrangeAbmCommand(Registration), TestContext.Current.CancellationToken);

        // Assert: nothing stored any more, and the Markdown untouched.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(RegistrationLayout.Read(Registration));
        Assert.Equal(markdown, await File.ReadAllBytesAsync(Body, TestContext.Current.CancellationToken));

        // Act + Assert: the undo gives back the dragged registration, and its redo arranges again.
        var undone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(result.Inverse), TestContext.Current.CancellationToken);
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Equal(dragged, await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken));
        var redone = await _dispatcher.DispatchAsync(Assert.IsAssignableFrom<ICommand>(undone.Inverse), TestContext.Current.CancellationToken);
        Assert.True(redone.IsSuccess, redone.Error);
        Assert.Empty(RegistrationLayout.Read(Registration));
    }

    [Fact]
    public async Task ArrangingWithNothingDragged_IsRefused_AndWritesNothing()
    {
        // Arrange.
        var before = await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken);

        // Act.
        var result = await _dispatcher.DispatchAsync(new ArrangeAbmCommand(Registration), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("already arranged", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllBytesAsync(Registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArrangingWithoutARegistration_IsRefused_AndCreatesNone()
    {
        // Arrange.
        File.Delete(Registration);

        // Act.
        var result = await _dispatcher.DispatchAsync(new ArrangeAbmCommand(Registration), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Registration));
    }

    [Fact]
    public void ALeafBetweenTwoBushes_TucksIn_SoTheBushesChildrenSitSideBySide()
    {
        // Arrange: P and Q each have three children, and the leaf L sits between them. Boxed
        // subtrees would leave a whole column empty under L; outlines let Q's children follow P's.
        var model = AbmParser.Parse(LineDocument.Parse(
            "## Behavior\n- **Do in order:** R\n" +
            "  - **Do in order:** P\n    - **Do:** P1\n    - **Do:** P2\n    - **Do:** P3\n" +
            "  - **Do:** L\n" +
            "  - **Do in order:** Q\n    - **Do:** Q1\n    - **Do:** Q2\n    - **Do:** Q3\n"));

        // Act.
        var positions = AbmLayout.Compute(model);

        // Assert: the order holds, and the two bushes' children are one step apart, not two.
        Assert.True(positions["1.1"].X < positions["1.2"].X && positions["1.2"].X < positions["1.3"].X);
        Assert.Equal(AbmLayout.NodeWidth + AbmLayout.HorizontalGap, positions["1.3.1"].X - positions["1.1.3"].X, 6);
        Assert.Equal((positions["1.1"].X + positions["1.3"].X) / 2, positions["1"].X, 6);
    }

    private async Task Dispatch(ICommand command)
    {
        var result = await _dispatcher.DispatchAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }
}
