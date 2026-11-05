using EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
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
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.FdgSessionTests");
    private readonly FdgDocumentStore _store = new(new FileWriter());
    private readonly List<DiagramDeltasEventArgs> _raised = [];

    public FdgSessionTests()
    {
        File.Copy(FieldServiceExample.Path, Body);
    }

    private string Body => _folder.Combine("field-service.fdg");

    public void Dispose()
    {
        _folder.Dispose();
    }

    [Fact]
    public async Task AFreshSession_DeliversEveryElementAndConnectionOfTheExample()
    {
        // Arrange.
        var model = FdgParser.Parse(LineDocument.Parse(await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken)));
        await using var session = Open();

        // Act.
        var delivered = (await session.BaselineAsync(TestContext.Current.CancellationToken)).AddedElements();

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
        var delivered = (await session.BaselineAsync(TestContext.Current.CancellationToken)).AddedElements().Where(element => !IsConnection(element)).ToList();

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
        var planning = (await session.BaselineAsync(TestContext.Current.CancellationToken)).AddedElements().Single(element => element.Id == "planning");
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
        await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Act.
        await EditAsync(new SetFdgSizeCommand(Body, "task-list", 220));

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
        await session.BaselineAsync(TestContext.Current.CancellationToken);

        // Act: a Description only.
        await EditAsync(new SetFdgDescriptionCommand(Body, "task-list", "A different description altogether."));

        // Assert.
        Assert.Empty(_raised);

        // Act, the presence that makes the absence mean something: a Name through the same path.
        await EditAsync(new RenameFdgElementCommand(Body, "task-list", "Today"));

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

    /// <summary>Writes the text <paramref name="command"/> makes of the file to disk, as another tool would, and has the store reload it.</summary>
    private async Task EditAsync(ICommand command)
    {
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        (string? edited, string? refusal) = await FdgEditing.RunAsync(text, command, TestContext.Current.CancellationToken);
        Assert.True(edited is not null, refusal);
        await File.WriteAllTextAsync(Body, edited, TestContext.Current.CancellationToken);
        await _store.ReloadAsync(Body, TestContext.Current.CancellationToken);
    }

    private static bool IsConnection(DiagramElement element) =>
        FdgConnectionTypes.All.Any(relation => element.Type == FdgElementMapper.ConnectionTypeOf(relation));

    private static FdgElementPayload ElementPayload(DiagramElement element) =>
        FdgElementPayload.Parser.ParseFrom(element.Payload.ToArray());
}
