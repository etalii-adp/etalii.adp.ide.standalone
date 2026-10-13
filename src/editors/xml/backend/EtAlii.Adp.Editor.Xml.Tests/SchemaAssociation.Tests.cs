using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>Which schema applies (xml-editor Requirement 5), asked of the analyser over the module's examples.</summary>
public sealed class SchemaAssociationTests : IDisposable
{
    private readonly ExampleProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact]
    public async Task NoNamespaceSchemaLocation_NamesTheSchemaOfElementsWithoutANamespace()
    {
        // Arrange and act.
        var analysis = await _project.AnalyzeAsync("library.xml");

        // Assert: the schema applies, is named in the status, and is a file the answer depended on.
        Assert.Empty(analysis.Findings);
        Assert.Equal("Schema: library.xsd", analysis.Status);
        Assert.Equal([_project.PathOf("library.xsd")], analysis.DependsOn);
    }

    [Fact]
    public async Task SchemaLocation_IsReadAsPairs_AndEverySchemaAppliesTogether()
    {
        // Arrange and act.
        var analysis = await _project.AnalyzeAsync("order.xml");

        // Assert: both namespaces named, and both schemas read.
        Assert.Empty(analysis.Findings);
        Assert.Equal("Schema: urn:example:orders, urn:example:parties", analysis.Status);
        Assert.Equal([_project.PathOf("order.xsd"), _project.PathOf("party.xsd")], analysis.DependsOn.Order());
    }

    [Fact]
    public async Task SchemaLocation_ValidatesTheElementsOfEachNamespace()
    {
        // Arrange: the party lacks its required name, which only party.xsd knows.
        var text = _project.Text("order.xml").Replace("    <p:name>Hendrik de Vries</p:name>\n", "", StringComparison.Ordinal);

        // Act.
        var analysis = await _project.AnalyzeAsync("order.xml", text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Contains("'name'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADoctype_NamesTheDtd_AndItsInternalSubsetIsRead()
    {
        // Arrange and act: recipe.xml uses &oven;, declared in its internal subset.
        var analysis = await _project.AnalyzeAsync("recipe.xml");

        // Assert.
        Assert.Empty(analysis.Findings);
        Assert.Equal("DTD: checked, no suggestions.", analysis.Status);
        Assert.Equal([_project.PathOf("recipe.dtd")], analysis.DependsOn);
    }

    [Fact]
    public async Task ADocumentThatIsASchema_IsCheckedAsOne()
    {
        // Arrange: a type that is declared nowhere.
        var text = _project.Text("library.xsd").Replace("type=\"xs:gYear\"", "type=\"year\"", StringComparison.Ordinal);

        // Act.
        var valid = await _project.AnalyzeAsync("library.xsd");
        var broken = await _project.AnalyzeAsync("library.xsd", text);

        // Assert.
        Assert.Empty(valid.Findings);
        Assert.Equal("XSD: compiles.", valid.Status);
        var finding = Assert.Single(broken.Findings);
        Assert.Equal(TextFindingSeverity.Error, finding.Severity);
        Assert.Contains("'year'", finding.Message, StringComparison.Ordinal);
        Assert.Equal("XSD: does not compile.", broken.Status);
    }

    [Theory]
    [InlineData("http://relaxng.org/ns/structure/1.0", "RELAX NG")]
    [InlineData("http://purl.oclc.org/dsdl/schematron", "Schematron")]
    public async Task AnXmlModelInstruction_IsSaidOnce_AsNotSupported(string type, string language)
    {
        // Arrange.
        var text = $"<?xml version=\"1.0\"?>\n<?xml-model href=\"notes.rng\" schematypens=\"{type}\"?>\n<notes/>\n";

        // Act.
        var analysis = await _project.AnalyzeAsync("notes.xml", text);

        // Assert: one information finding where it is named, and no schema findings.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Info, finding.Severity);
        Assert.Equal("xml-model", text[finding.From..finding.To]);
        Assert.Contains(language, finding.Message, StringComparison.Ordinal);
        Assert.Equal("No schema", analysis.Status);
    }

    [Fact]
    public async Task AnXsdThatDeclaresVersion11_IsSaidOnce_AsNotSupported()
    {
        // Arrange.
        _project.Write("library.xsd", _project.Text("library.xsd").Replace(
            "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">",
            "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:vc=\"http://www.w3.org/2007/XMLSchema-versioning\" vc:minVersion=\"1.1\">",
            StringComparison.Ordinal));

        // Act.
        var analysis = await _project.AnalyzeAsync("library-broken.xml");

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Info, finding.Severity);
        Assert.Contains("XSD 1.1", finding.Message, StringComparison.Ordinal);
        Assert.Equal("library.xsd", _project.Text("library-broken.xml")[finding.From..finding.To]);
    }

    [Theory]
    [InlineData("../../library.xsd", "'../../library.xsd' leaves the project.")]
    [InlineData("http://example.com/library.xsd", "'http://example.com/library.xsd' is a web address; ADP reads schemas from the project.")]
    [InlineData("missing.xsd", "'missing.xsd' is not in the project, so no schema is checked from it.")]
    public async Task AReferenceThatCannotBeResolved_IsOneInformationFindingWhereItIsNamed(string reference, string message)
    {
        // Arrange.
        var text = _project.Text("library-broken.xml").Replace("\"library.xsd\"", $"\"{reference}\"", StringComparison.Ordinal);

        // Act.
        var analysis = await _project.AnalyzeAsync("library-broken.xml", text);

        // Assert: said where it is named, and nothing from a schema.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Info, finding.Severity);
        Assert.Equal(reference, text[finding.From..finding.To]);
        Assert.Equal(message, finding.Message);
        Assert.Equal("No schema", analysis.Status);
    }

    [Fact]
    public async Task ADocumentThatNamesNoSchema_SaysSo()
    {
        // Arrange and act.
        var analysis = await _project.AnalyzeAsync("notes.xml");

        // Assert.
        Assert.Empty(analysis.Findings);
        Assert.Equal("No schema", analysis.Status);
        Assert.Empty(analysis.DependsOn);
    }

    [Fact]
    public async Task AChangedIncludedFile_ChangesTheNextAnswer_WithNoRestart()
    {
        // Arrange: library.xsd takes its book type from an include.
        var schema = _project.Text("library.xsd");
        var bookStart = schema.IndexOf("  <xs:complexType name=\"book\">", StringComparison.Ordinal);
        var bookEnd = schema.IndexOf("</xs:schema>", StringComparison.Ordinal);
        _project.Write("book.xsd", $"<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">\n{schema[bookStart..bookEnd]}</xs:schema>\n");
        // An include comes before every declaration, so it goes straight after the schema's start tag.
        var opened = schema.IndexOf('>', schema.IndexOf("<xs:schema", StringComparison.Ordinal)) + 1;
        _project.Write("library.xsd", schema[..opened] + "\n  <xs:include schemaLocation=\"book.xsd\"/>" + schema[opened..bookStart] + schema[bookEnd..]);
        var before = await _project.AnalyzeAsync("library.xml");

        // Act: the include now requires a shelfmark nobody has.
        _project.Write("book.xsd", _project.Text("book.xsd").Replace(
            "<xs:element name=\"lent\"",
            "<xs:element name=\"shelfmark\" type=\"xs:string\"/>\n      <xs:element name=\"lent\"",
            StringComparison.Ordinal));
        var after = await _project.AnalyzeAsync("library.xml");

        // Assert.
        Assert.Empty(before.Findings);
        Assert.Contains(_project.PathOf("book.xsd"), before.DependsOn);
        Assert.NotEmpty(after.Findings);
        Assert.All(after.Findings, finding => Assert.Contains("shelfmark", finding.Message, StringComparison.Ordinal));
    }
}
