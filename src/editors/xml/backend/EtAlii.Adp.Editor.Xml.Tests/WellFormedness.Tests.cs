using System.Text;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// Mistakes in the XML itself (xml-editor Requirement 4), asked of the module's analyser as the
/// editor service asks it: one finding where the reader stopped, with the platform's sentence,
/// at an offset into the text as sent whether its lines end in CRLF or LF.
/// </summary>
public sealed class WellFormednessTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("xml-wellformed");

    public void Dispose() => _folder.Dispose();

    public static TheoryData<string> LineEndings() => new() { "\n", "\r\n" };

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task AnEndTagThatDoesNotMatch_IsMarkedWhereTheMismatchIs_NamingBoth(string newline)
    {
        // Arrange.
        var text = $"<library>{newline}  <book>{newline}  </library>{newline}";

        // Act.
        var analysis = await Analyze(text);

        // Assert: on the end tag's name, not at the end of the file, and both tags named.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Error, finding.Severity);
        Assert.Equal("library", text[finding.From..finding.To]);
        Assert.Equal(text.LastIndexOf("library", StringComparison.Ordinal), finding.From);
        Assert.Contains("'book'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'library'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task ARepeatedAttribute_MarksTheSecond(string newline)
    {
        // Arrange.
        var text = $"<library>{newline}  <book isbn=\"1\" isbn=\"2\"/>{newline}</library>";

        // Act.
        var analysis = await Analyze(text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(text.LastIndexOf("isbn", StringComparison.Ordinal), finding.From);
        Assert.Equal("isbn", text[finding.From..finding.To]);
        Assert.Contains("'isbn' is a duplicate attribute name", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public async Task AnUndeclaredPrefix_MarksTheName(string newline)
    {
        // Arrange.
        var text = $"<library>{newline}  <p:book/>{newline}</library>";

        // Act.
        var analysis = await Analyze(text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(text.IndexOf("p:book", StringComparison.Ordinal), finding.From);
        Assert.Contains("'p' is an undeclared prefix", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSentence_LeavesOutThePositionTheUnderlineShows()
    {
        // Arrange and act.
        var analysis = await Analyze("<a></b>");

        // Assert.
        Assert.DoesNotContain("Line 1, position", Assert.Single(analysis.Findings).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterTheFirstError_TheStatusSaysTheRestWasNotChecked()
    {
        // Arrange: two mistakes; the reader stops at the first and reports one.
        var text = "<a>\n<b></c>\n<d x='1' x='2'/>\n</a>";

        // Act.
        var analysis = await Analyze(text);

        // Assert.
        Assert.Single(analysis.Findings);
        Assert.Equal("Not well-formed: the rest was not checked.", analysis.Status);
    }

    [Fact]
    public async Task AWellFormedDocument_HasNoFinding()
    {
        // Arrange and act.
        var analysis = await Analyze("<?xml version=\"1.0\"?>\r\n<a x='1'><b/><!-- c --><![CDATA[<d>]]></a>\r\n");

        // Assert.
        Assert.Empty(analysis.Findings);
        Assert.NotEqual("Not well-formed: the rest was not checked.", analysis.Status);
    }

    [Fact]
    public async Task ADeclaredEncodingOtherThanTheStoredOne_IsAWarningOnTheDeclaration()
    {
        // Arrange: stored as UTF-8 with a character outside ASCII, declared as ISO-8859-1.
        var text = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<greeting>Café</greeting>\n";

        // Act.
        var analysis = await Analyze(text);

        // Assert.
        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(TextFindingSeverity.Warning, finding.Severity);
        Assert.Equal("ISO-8859-1", text[finding.From..finding.To]);
        Assert.Equal("The declaration says the file is ISO-8859-1, but it is stored as UTF-8.", finding.Message);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<g>Café</g>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\n<g>Goedendag</g>\n")]
    public async Task ADeclarationThatAgreesWithTheStoredBytes_IsNotMarked(string text)
    {
        // Arrange and act: UTF-8 as declared, and ASCII bytes that read the same in ISO-8859-1.
        var analysis = await Analyze(text);

        // Assert.
        Assert.Empty(analysis.Findings);
    }

    private async Task<TextAnalysis> Analyze(string text)
    {
        var path = _folder.Combine("document.xml");
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), TestContext.Current.CancellationToken);
        return await new XmlAnalyzer().AnalyzeAsync(ProjectPath.Within(ProjectRoot.From(_folder.Path), path)!.Value, text, TestContext.Current.CancellationToken);
    }
}
