using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Criterion 5.4 and finding B14, repaired on purpose (Q6): the first element and the first connection can be
/// added to a new document from the binding's template, and to one whose empty list carries a trailing comment,
/// through the real store on a real file.
/// </summary>
/// <remarks>
/// <para>
/// <b>The trailing-comment cases were seen to fail against the commit before task 10</b>, where
/// <c>FdgWriter</c> wrote the line: it recognised <c>[]</c> and <c>[ ]</c> only, so <c>elements: [] # none
/// yet</c> got an item under a key that still held <c>[]</c>, and the file no longer read.
/// </para>
/// <para>
/// <b>The template cases are the criterion, not the defect</b>: <c>FdgWriter</c> opened a bare <c>[]</c>
/// too, so they pass against that commit. They were seen to fail instead against the library with its
/// opening of an empty flow sequence switched off, which is what they hold.
/// </para>
/// </remarks>
public sealed class FdgFirstEntryTests : IDisposable
{
    /// <summary>Two elements, and an empty connections list that carries a comment.</summary>
    private const string NoConnectionsYet =
        "functional-decomposition-graph: 1\r\n" +
        "elements:\r\n" +
        "  - id: page\r\n" +
        "    type: ui-element\r\n" +
        "    name: Page\r\n" +
        "    x: 0\r\n" +
        "    y: 0\r\n" +
        "    width: 140\r\n" +
        "  - id: part\r\n" +
        "    type: ui-element\r\n" +
        "    name: Part\r\n" +
        "    x: 0\r\n" +
        "    y: 100\r\n" +
        "    width: 140\r\n" +
        "connections: [] # none yet\r\n";

    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.FdgFirstEntryTests");
    private readonly FdgDocumentStore _store = new(new FileWriter());

    private string Body => _folder.Combine("first-entry.fdg");

    public void Dispose()
    {
        _folder.Dispose();
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>The documents a first element is added to: the template, and <c>elements: [] # none yet</c>.</summary>
    public static TheoryData<string> EveryEmptyElementList => ["the template", "a list with a trailing comment"];

    private static string EmptyElementList(string name) => name switch
    {
        "the template" => FdgBody.Template,
        "a list with a trailing comment" => Fixture("empty-list-comment.fdg"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such document."),
    };

    /// <summary>The first element, then a second, then the first connection between them, each read back.</summary>
    [Theory]
    [MemberData(nameof(EveryEmptyElementList))]
    public async Task TheFirstElementAndConnection_AreAdded(string document)
    {
        // Arrange.
        await File.WriteAllTextAsync(Body, EmptyElementList(document), TestContext.Current.CancellationToken);

        // Act and assert: each step is written and the document still reads with what it added.
        await Run(new AddFdgElementCommand(Body, "ui-element", 240, 160, "first"));
        Assert.NotNull(FdgDefinition.ElementOf(await Read(), "first"));

        await Run(new AddFdgElementCommand(Body, "ui-element", 240, 360, "second"));
        Assert.NotNull(FdgDefinition.ElementOf(await Read(), "second"));

        await Run(new ConnectFdgElementsCommand(Body, "ui-child", "first", "second", "link"));
        AssertLinked(await Read(), "link", "first", "second");
    }

    /// <summary>The first connection into <c>connections: [] # none yet</c>, beside elements that are already there.</summary>
    [Fact]
    public async Task TheFirstConnection_IsAdded_ToAListWithATrailingComment()
    {
        // Arrange.
        await File.WriteAllTextAsync(Body, NoConnectionsYet, TestContext.Current.CancellationToken);

        // Act.
        await Run(new ConnectFdgElementsCommand(Body, "ui-child", "page", "part", "link"));

        // Assert.
        AssertLinked(await Read(), "link", "page", "part");
    }

    private async Task Run(ICommand command)
    {
        var result = await new FdgTestDispatcher(_store).DispatchAsync(command, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    private async Task<DislDiagram> Read()
    {
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        var body = FdgBody.Parse(text);
        Assert.False(body.IsUnreadable, $"The document no longer reads:\n{text}");
        return body.Disl.Diagram;
    }

    private static void AssertLinked(DislDiagram diagram, string connection, string from, string to)
    {
        var link = Assert.IsType<DislElement>(FdgDefinition.ElementOf(diagram, connection), exactMatch: false);
        Assert.True(link.Type.IsRelation);
        Assert.Equal(from, FdgDefinition.WrittenId(Assert.IsType<DislElement>(link.Source, exactMatch: false)));
        Assert.Equal(to, FdgDefinition.WrittenId(Assert.IsType<DislElement>(link.Target, exactMatch: false)));
    }
}
