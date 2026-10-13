using System.Text.RegularExpressions;
using System.Xml;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// What the XSD offers at the caret (xml-editor Requirement 7), asked of the analyser over the
/// module's own examples as the editor service asks it.
/// </summary>
public sealed partial class XmlCompletionTests : IDisposable
{
    private const string Library = "<?xml version=\"1.0\"?>\n<library xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"library.xsd\">\n";

    private readonly ExampleProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public async Task AfterALessThan_TheElementsTheContentModelAllowsThere_AreOffered()
    {
        // Act: before the name, and once the name is written.
        var first = await _project.CompleteAsync("library.xml", Library + "  <|");
        var next = await _project.CompleteAsync("library.xml", Library + "  <name>Shelf</name>\n  <|");

        // Assert: Requirement 7.1 - by the content model and the children already present.
        Assert.Equal(["name"], first.Select(item => item.Label));
        Assert.Equal(["book"], next.Select(item => item.Label));
    }

    [Fact]
    public async Task AskedForInContent_ElementsAreOfferedWithTheirLessThan_AndNotWhileTyping()
    {
        // Act.
        var asked = await _project.CompleteAsync("library.xml", Library + "  |", isExplicit: true);
        var typing = await _project.CompleteAsync("library.xml", Library + "  |");

        // Assert.
        var item = Assert.Single(asked);
        Assert.Equal("<name>${1}</name>", item.Insert);
        Assert.Empty(typing);
    }

    [Fact]
    public async Task InAStartTag_TheAttributesNotYetPresent_AreOffered_RequiredFirst()
    {
        // Act.
        var all = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book |");
        var rest = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book isbn=\"1\" |");

        // Assert: Requirement 7.2.
        Assert.Equal(["isbn", "format"], all.OrderByDescending(item => item.Boost).Select(item => item.Label));
        Assert.True(all.Single(item => item.Label == "isbn").Boost > all.Single(item => item.Label == "format").Boost);
        Assert.Equal("required", all.Single(item => item.Label == "isbn").Detail);
        Assert.Equal(["format"], rest.Select(item => item.Label));
        Assert.Equal("format=\"${1}\"", Assert.Single(rest).Insert);
    }

    [Fact]
    public async Task InAValue_AnEnumerationsValues_AndABooleansTwo_AreOffered()
    {
        // Act.
        var attribute = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book isbn=\"1\" format=\"pa|\">");
        var text = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book isbn=\"1\">\n    <title>t</title>\n    <author>a</author>\n    <year>1900</year>\n    <lent>|");

        // Assert: Requirement 7.3, each replacing what was typed of the value.
        Assert.Equal(["hardback", "paperback", "ebook"], attribute.Select(item => item.Label));
        Assert.All(attribute, item => Assert.Equal(2, item.To - item.From));
        Assert.Equal(["true", "false"], text.Select(item => item.Label));
    }

    [Theory]
    [InlineData("  <|")]
    [InlineData("  <name>n</name>\n  <|")]
    [InlineData("  <name>n</name>\n  <book isbn=\"1\">\n    <title>t</title>\n    <author>a</author>\n    <|")]
    [InlineData("  <name>n</name>\n  <book isbn=\"1\">\n    <title>t</title>\n    <author>a</author>\n    <year>1900</year>\n    <|")]
    public async Task EveryElementAccepted_LeavesTheDocumentWellFormed(string marked)
    {
        // Arrange: a document that is well-formed once something stands where the caret is.
        var before = Library + marked;
        var open = Regex.Matches(before, @"<(\w+)\b[^>]*(?<!/)>").Select(match => match.Groups[1].Value).ToList();
        foreach (Match closed in Regex.Matches(before, @"</(\w+)>"))
        {
            open.Remove(closed.Groups[1].Value);
        }

        var after = string.Concat(Enumerable.Reverse(open).Select(name => $"\n</{name}>"));

        // Act.
        var items = await _project.CompleteAsync("library.xml", before + after);

        // Assert: Requirement 7.4, for every item offered.
        Assert.NotEmpty(items);
        var text = (before + after).Remove(before.IndexOf('|', StringComparison.Ordinal), 1);
        foreach (var item in items)
        {
            var completed = text[..item.From] + TabStop().Replace(item.Insert, "") + text[item.To..];
            Assert.Null(Record.Exception(() => ReadToTheEnd(completed)));
        }
    }

    [Fact]
    public async Task AnElement_IsInsertedWithItsRequiredAttributes_SelfClosingWhereItHoldsNothing()
    {
        // Act.
        var book = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <|");
        var signed = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book isbn=\"1\">\n    <title>t</title>\n    <author>a</author>\n    <year>1900</year>\n    <|");

        // Assert: Requirement 7.4 - the caret's first stop is the first required value.
        Assert.Equal("book isbn=\"${1}\">${2}</book>", Assert.Single(book).Insert);
        Assert.Equal("signed/>", signed.Single(item => item.Label == "signed").Insert);
        Assert.Equal("lent>${1}</lent>", signed.Single(item => item.Label == "lent").Insert);
    }

    [Fact]
    public async Task AnElementOfANamespace_IsOfferedWithTheDocumentsPrefix()
    {
        // Arrange.
        var order = _project.Text("order.xml");
        var marked = order[..(order.IndexOf("  <p:party>", StringComparison.Ordinal) + 2)] + "<|";

        // Act.
        var items = await _project.CompleteAsync("order.xml", marked);

        // Assert: Requirement 7.5.
        var item = Assert.Single(items);
        Assert.Equal("p:party", item.Label);
        Assert.Equal("p:party>${1}</p:party>", item.Insert);
        Assert.Equal("", item.Detail);
    }

    [Fact]
    public async Task AnElementOfANamespaceTheDocumentDoesNotDeclare_IsOfferedWithANote_AndNoDeclaration()
    {
        // Arrange: order.xml without its declaration of the parties' namespace.
        var order = _project.Text("order.xml").Replace(" xmlns:p=\"urn:example:parties\"", "", StringComparison.Ordinal);
        var marked = order[..(order.IndexOf("  <p:party>", StringComparison.Ordinal) + 2)] + "<|";

        // Act.
        var items = await _project.CompleteAsync("order.xml", marked);

        // Assert: Requirement 7.5.
        var item = Assert.Single(items);
        Assert.Equal("party", item.Label);
        Assert.Equal("namespace not declared", item.Detail);
        Assert.DoesNotContain("xmlns", item.Insert, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSchemasDocumentation_IsBesideTheItem_AndFoundForANameAlreadyWritten()
    {
        // Act: offered, and asked at a name already in the document, as a hover asks.
        var offered = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <|");
        var attributes = await _project.CompleteAsync("library.xml", Library + "  <name>n</name>\n  <book |");
        var library = _project.Text("library.xml");
        var atAName = library.IndexOf("<book", StringComparison.Ordinal) + 3;
        var hovered = await _project.CompleteAsync("library.xml", library.Insert(atAName, "|"));

        // Assert: Requirement 7.6.
        Assert.Equal("One book on the shelf.", Assert.Single(offered).Info);
        Assert.Equal("The book's ISBN, with or without dashes.", attributes.Single(item => item.Label == "isbn").Info);
        var written = hovered.Single(item => item.Label == "book");
        Assert.Equal("One book on the shelf.", written.Info);
        Assert.Equal("book", written.Insert);
    }

    [Theory]
    [InlineData("  <!-- <|")]
    [InlineData("  <name>n</name>\n  <book isbn=\"1\"></boko>\n  <|")]
    [InlineData("  <name>n</name>\n  <book isbn=\"1\" isbn=\"2\">\n    <|")]
    public async Task WhereTheTextBeforeTheCaretHidesThePlace_NothingIsOffered(string marked)
    {
        // Act.
        var items = await _project.CompleteAsync("library.xml", Library + marked, isExplicit: true);

        // Assert: Requirement 7.7.
        Assert.Empty(items);
    }

    [Theory]
    [InlineData("recipe.xml", "<recipe>")]
    [InlineData("notes.xml", "<notes>")]
    public async Task WithADtdOrNoSchema_NothingIsOffered(string file, string after)
    {
        // Arrange.
        var text = _project.Text(file);
        var marked = text.Insert(text.IndexOf(after, StringComparison.Ordinal) + after.Length, "\n  <|");

        // Act.
        var items = await _project.CompleteAsync(file, marked, isExplicit: true);

        // Assert: Requirement 7.8 - the end tag is the client's.
        Assert.Empty(items);
    }

    private static void ReadToTheEnd(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        while (reader.Read())
        {
        }
    }

    [GeneratedRegex(@"\$\{\d+\}")]
    private static partial Regex TabStop();
}
