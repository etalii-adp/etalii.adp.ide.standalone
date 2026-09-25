using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// A session over the field-service example, through the real store on a real file (task 11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every change is made the way an edit makes it</b>: with task 9's own <see cref="FdgWriter"/>,
/// written to disk, then reloaded through the store - so the path under test is the file, the
/// lifecycle, the change handler and the mapper, not a model handed in by the test.
/// </para>
/// <para>
/// <b>An absence is only asserted beside a presence.</b> "A description change raises nothing" is
/// worthless if nothing would have been raised anyway, so the same test then makes a change that
/// must raise, and asserts that it does.
/// </para>
/// </remarks>
public sealed class FdgSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.FdgSessionTests", Guid.NewGuid().ToString("N"));
    private readonly FdgDocumentStore _store = new();
    private readonly List<DiagramDeltasEventArgs> _raised = [];

    public FdgSessionTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(FieldServiceExample.Path, Body);
    }

    private string Body => Path.Combine(_folder, "field-service.fdg");

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
    public async Task AFreshSession_DeliversEveryElementAndConnectionOfTheExample()
    {
        // Arrange.
        var model = FdgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));
        await using var session = Open();

        // Act.
        var delivered = Delivered(session.Baseline());

        // Assert.
        var expected = model.Elements.Select(element => element.Id).Concat(model.Connections.Select(connection => connection.Id));
        Assert.Equal(expected.Order(StringComparer.Ordinal), delivered.Select(element => element.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EveryElementArrivesAtTheSharedHeight_AndACommentAtItsOwn()
    {
        // Arrange.
        await using var session = Open();

        // Act.
        var delivered = Delivered(session.Baseline()).Where(element => !IsConnection(element)).ToList();

        // Assert.
        Assert.NotEmpty(delivered);
        Assert.All(delivered, element =>
        {
            var height = ElementPayload(element).Height;
            if (element.Type == FdgElementMapper.CommentType)
            {
                Assert.Equal(96, height);
            }
            else
            {
                Assert.Equal(FdgGeometry.SharedHeight, height);
            }
        });
    }

    [Fact]
    public async Task AnElementArrivesAtItsCentre_WithItsSizeInThePayload()
    {
        // Arrange: the example's Planning element sits at top-left (600, 40), 140 wide.
        await using var session = Open();

        // Act.
        var planning = Delivered(session.Baseline()).Single(element => element.Id == "planning");
        var payload = ElementPayload(planning);

        // Assert.
        Assert.Equal(FdgElementMapper.UiElementType, planning.Type);
        Assert.Equal(600 + (140 / 2d), planning.X);
        Assert.Equal(40 + (FdgGeometry.SharedHeight / 2), planning.Y);
        Assert.Equal(140, payload.Width);
        Assert.Equal("Planning", payload.Name);
    }

    /// <summary>
    /// A size lives in the payload because the core element has no field for it, so a resize must
    /// change the payload bytes the shared diff compares. Written against the payload that dropped
    /// the size, under which this raises nothing.
    /// </summary>
    [Fact]
    public async Task AResizeOnDisk_ArrivesAsADelta()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();

        // Act.
        Edit("task-list", (document, element) => FdgWriter.Resize(document, element, 220, null));

        // Assert.
        var changed = Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(Assert.Single(_raised).Deltas)).Elements);
        Assert.Equal("task-list", changed.Id);
        Assert.Equal(220, ElementPayload(changed).Width);
    }

    /// <summary>
    /// Requirement 7.1: a Description is never sent, so a change to one alone changes nothing sent
    /// and raises no delta at all. Seen to fail against a mapper that packs the Description into the
    /// payload - the change then arrives as a delta.
    /// </summary>
    [Fact]
    public async Task ADescriptionChangedOnDisk_RaisesNothing_WhileANameChangeDoes()
    {
        // Arrange.
        await using var session = Open();
        session.Baseline();

        // Act: a Description only.
        Edit("task-list", (document, element) => FdgWriter.SetDescription(document, element, "A different description altogether."));

        // Assert.
        Assert.Empty(_raised);

        // Act, the presence that makes the absence mean something: a Name through the same path.
        Edit("task-list", (document, element) => FdgWriter.SetName(document, element, "Today"));

        // Assert.
        var changed = Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(Assert.Single(_raised).Deltas)).Elements);
        Assert.Equal("Today", ElementPayload(changed).Name);
    }

    /// <summary>The payloads cannot carry a Description at all: there is no field to put one in.</summary>
    [Fact]
    public void NeitherPayload_HasAFieldForADescription()
    {
        Assert.DoesNotContain(FdgElementPayload.Descriptor.Fields.InDeclarationOrder(), field => field.Name.Contains("description", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FdgConnectionPayload.Descriptor.Fields.InDeclarationOrder(), field => field.Name.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    private FdgSession Open()
    {
        var session = new FdgSession(Body, _store, new FdgElementMapper());
        session.Changed += (_, args) => _raised.Add(args);
        return session;
    }

    private void Edit(string elementId, Func<LineDocument, FdgElement, FdgEdit> edit)
    {
        var document = LineDocument.Parse(File.ReadAllText(Body));
        var element = FdgParser.Parse(document).Elements.Single(candidate => candidate.Id == elementId);
        Assert.True(edit(document, element).WasApplied);
        File.WriteAllText(Body, document.Text);
        _store.Reload(Body);
    }

    private static IReadOnlyList<DiagramElement> Delivered(IReadOnlyList<DiagramDelta> baseline) =>
        Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;

    private static bool IsConnection(DiagramElement element) =>
        FdgConnectionTypes.All.Any(relation => element.Type == FdgElementMapper.ConnectionTypeOf(relation));

    private static FdgElementPayload ElementPayload(DiagramElement element) =>
        FdgElementPayload.Parser.ParseFrom(element.Payload.ToArray());
}
