using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// Validation against the schema (xml-editor Requirement 6 and 8.2): each place the validator
/// reports, on the element name, the element that lacks something, or the value; nothing from a
/// schema for a text that is not well-formed or a schema that does not compile.
/// </summary>
public sealed class SchemaValidationTests : IDisposable
{
    private readonly ExampleProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public async Task TheBrokenLibrary_IsMarkedWhereEachMistakeIs()
    {
        // Arrange.
        var text = _project.Text("library-broken.xml");

        // Act.
        var analysis = await _project.AnalyzeAsync("library-broken.xml", text);

        // Assert.
        Assert.Equal(4, analysis.Findings.Count);
        Assert.All(analysis.Findings, finding => Assert.Equal(TextFindingSeverity.Error, finding.Severity));

        // A required attribute missing: on the start tag of the book that lacks it.
        var attribute = analysis.Findings.Single(finding => finding.Message.Contains("required attribute 'isbn'", StringComparison.Ordinal));
        Assert.Equal(text.IndexOf("<book format", StringComparison.Ordinal) + 1, attribute.From);

        // A required child missing: on the start tag - not the end tag - of the book that lacks it, naming it.
        var child = analysis.Findings.Single(finding => finding.Message.Contains("incomplete content", StringComparison.Ordinal));
        Assert.Equal(text.IndexOf("<book isbn=\"978-0-19", StringComparison.Ordinal) + 1, child.From);
        Assert.Equal("book", text[child.From..child.To]);
        Assert.Contains("year", child.Message, StringComparison.Ordinal);

        // A value that does not fit its type: on the value.
        var value = analysis.Findings.Single(finding => finding.Message.Contains("'soon'", StringComparison.Ordinal));
        Assert.Equal("soon", text[value.From..value.To]);

        // An element not allowed where it stands: on its name, saying what was expected.
        var element = analysis.Findings.Single(finding => finding.Message.Contains("'publisher'", StringComparison.Ordinal));
        Assert.Equal("publisher", text[element.From..element.To]);
        Assert.Equal(text.IndexOf("<publisher", StringComparison.Ordinal) + 1, element.From);
        Assert.Contains("expected: 'book'", element.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAttributeValueThatDoesNotFit_IsMarkedOnTheValue()
    {
        // Arrange.
        var text = _project.Text("library.xml").Replace("format=\"paperback\"", "format=\"vinyl\"", StringComparison.Ordinal);

        // Act.
        var analysis = await _project.AnalyzeAsync("library.xml", text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("vinyl", text[finding.From..finding.To]);
    }

    [Fact]
    public async Task ATextThatIsNotWellFormed_GetsNoSchemaFinding()
    {
        // Arrange: the broken library, with its last end tag misspelt as well.
        var text = _project.Text("library-broken.xml").Replace("</library>", "</librar>", StringComparison.Ordinal);

        // Act.
        var analysis = await _project.AnalyzeAsync("library-broken.xml", text);

        // Assert: the well-formedness finding alone, and the status says the schema was not checked.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(WellFormedness.Source, finding.Source);
        Assert.Equal("Not well-formed: the rest was not checked. Schema not checked.", analysis.Status);
    }

    [Fact]
    public async Task ASchemaThatDoesNotCompile_IsSaidOnce_AndGivesNoSchemaFinding()
    {
        // Arrange.
        _project.Write("library.xsd", _project.Text("library.xsd").Replace("type=\"format\"", "type=\"medium\"", StringComparison.Ordinal));

        // Act.
        var analysis = await _project.AnalyzeAsync("library-broken.xml");

        // Assert: one finding where the schema is named, naming the file and its first error.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Info, finding.Severity);
        Assert.StartsWith("'library.xsd' does not compile: ", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'medium'", finding.Message, StringComparison.Ordinal);
        Assert.Equal("Schema: library.xsd does not compile.", analysis.Status);
    }

    [Fact]
    public async Task ADtd_Validates()
    {
        // Arrange: a recipe without its title.
        var text = _project.Text("recipe.xml").Replace("  <title>Soda bread</title>\n", "", StringComparison.Ordinal);

        // Act.
        var analysis = await _project.AnalyzeAsync("recipe.xml", text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Error, finding.Severity);
        // The first ingredient is the element not allowed where it stands (Requirement 7.2), and
        // the sentence says the title was expected there.
        Assert.Equal("ingredient", text[finding.From..finding.To]);
        Assert.Contains("'title'", finding.Message, StringComparison.Ordinal);
        Assert.Equal("DTD: checked, no suggestions.", analysis.Status);
    }

    [Fact]
    public async Task NothingRead_IsWrittenBack_NotEvenADefaultTheSchemaSupplies()
    {
        // Arrange: the schema supplies a default format for every book.
        _project.Write("library.xsd", _project.Text("library.xsd").Replace(
            "<xs:attribute name=\"format\" type=\"format\"/>",
            "<xs:attribute name=\"format\" type=\"format\" default=\"paperback\"/>",
            StringComparison.Ordinal));
        var examples = Directory.EnumerateFiles(_project.Root).Order().ToArray();
        var before = new List<byte[]>();
        foreach (var file in examples)
        {
            before.Add(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        }

        // Act: every example analysed, then saved back as it was read, as the family saves.
        foreach (var file in examples)
        {
            await _project.AnalyzeAsync(Path.GetFileName(file));
            var opened = TextFileBuffer.Open(file);
            Assert.NotNull(opened.Buffer);
            Assert.Equal("", await opened.Buffer.SaveAsync(opened.Buffer.Content, TestContext.Current.CancellationToken));
        }

        // Assert.
        for (var index = 0; index < examples.Length; index++)
        {
            Assert.Equal(before[index], await File.ReadAllBytesAsync(examples[index], TestContext.Current.CancellationToken));
        }

        Assert.DoesNotContain("format=\"paperback\">\n    <title>The Moonstone", _project.Text("library.xml"), StringComparison.Ordinal);
    }
}
