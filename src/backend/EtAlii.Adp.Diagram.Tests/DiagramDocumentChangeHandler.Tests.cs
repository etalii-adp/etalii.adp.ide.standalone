using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The shared document-change handler (backend-centralization R5.1).
/// </summary>
public sealed class DiagramDocumentChangeHandlerTests : IDisposable
{
    private const string First = @"C:\projects\one\first.sample";
    private const string Second = @"C:\projects\one\second.sample";

    private readonly LogCapture _logger = LogCapture.Start();

    public void Dispose() => _logger.Dispose();

    [Fact]
    public void TwoSessionsOnTwoPaths_OnlyTheChangedOneHears()
    {
        // Every session subscribes to its module's one store, so every session is told about
        // every document that store holds. The path is the only thing that keeps one diagram's
        // edit off another's canvas.
        var first = new Session(First);
        var second = new Session(Second);
        first.Handler.Deliver();
        second.Handler.Deliver();
        first.Document = [Element("a"), Element("b")];
        second.Document = [Element("x"), Element("y")];

        first.Handler.OnDocumentChanged(First);
        second.Handler.OnDocumentChanged(First);

        // The first hears it - which is what makes the second's silence mean something.
        Assert.Single(first.Raised);
        Assert.Empty(second.Raised);
    }

    [Fact]
    public void ThePathComparison_IgnoresCase()
    {
        var session = new Session(First);
        session.Handler.Deliver();
        session.Document = [Element("a"), Element("b")];

        session.Handler.OnDocumentChanged(First.ToUpperInvariant());

        Assert.Single(session.Raised);
    }

    [Fact]
    public void AChange_RaisesTheDiffAgainstWhatWasDelivered()
    {
        var session = new Session(First) { Document = [Element("a"), Element("b")] };
        session.Handler.Deliver();
        session.Document = [Element("a", x: 99)];

        session.Handler.OnDocumentChanged(First);

        var deltas = Assert.Single(session.Raised);
        Assert.Equal(["b"], Assert.IsType<DiagramRemoveDelta>(deltas[0]).ElementIds);
        Assert.Equal(99, Assert.Single(Assert.IsType<DiagramAddDelta>(deltas[1]).Elements).X);
        Assert.Equal(["a"], session.Handler.Delivered.Select(element => element.Id));
    }

    [Fact]
    public void AChangeThatChangesNothingVisible_RaisesNothing()
    {
        var session = new Session(First) { Document = [Element("a")] };
        session.Handler.Deliver();

        session.Handler.OnDocumentChanged(First);

        Assert.Empty(session.Raised);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void AReadFailure_IsLoggedWithItsPath_AndRaisesNothing(Type failure)
    {
        var session = new Session(First) { Document = [Element("a")] };
        session.Handler.Deliver();
        session.Failure = (Exception)Activator.CreateInstance(failure, "locked")!;

        session.Handler.OnDocumentChanged(First);

        Assert.Empty(session.Raised);
        Assert.Contains(_logger.Warnings, line => line.Contains(First, StringComparison.Ordinal));

        // And what was delivered is untouched, so the next change diffs from the right place.
        Assert.Equal(["a"], session.Handler.Delivered.Select(element => element.Id));
    }

    [Fact]
    public void AnyOtherFailure_ReachesTheCaller()
    {
        // R5.2: only read failures are caught. The reload bridge guards each document's reload,
        // so an unexpected failure is logged there with its path instead of swallowed here.
        var session = new Session(First) { Document = [Element("a")] };
        session.Handler.Deliver();
        session.Failure = new InvalidOperationException("a defect");

        Assert.Throws<InvalidOperationException>(() => session.Handler.OnDocumentChanged(First));
        Assert.Empty(session.Raised);
    }

    [Fact]
    public void AViewportRefresh_ReturnsTheDeltas_AndRaisesNothing()
    {
        var session = new Session(First) { Document = [Element("a")] };
        session.Handler.Deliver();
        session.Document = [Element("a"), Element("b")];

        var deltas = session.Handler.Refresh();

        Assert.Equal("b", Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(deltas)).Elements).Id);
        Assert.Empty(session.Raised);
        Assert.Equal(["a", "b"], session.Handler.Delivered.Select(element => element.Id));
    }

    private static DiagramElement Element(string id, double x = 10) =>
        new(id, x, 20, "test/sample+node", "type.googleapis.com/test.Sample", new byte[] { 1 });

    /// <summary>A session as the handler sees it: a render over a document it can be told to fail reading.</summary>
    private sealed class Session
    {
        public Session(string bodyPath) =>
            Handler = new DiagramDocumentChangeHandler(bodyPath, Render, Raised.Add);

        public DiagramDocumentChangeHandler Handler { get; }

        public IReadOnlyList<DiagramElement> Document { get; set; } = [];

        public Exception? Failure { get; set; }

        public List<IReadOnlyList<DiagramDelta>> Raised { get; } = [];

        private IReadOnlyList<DiagramElement> Render() => Failure is null ? Document : throw Failure;
    }
}
