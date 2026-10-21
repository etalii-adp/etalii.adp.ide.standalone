using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>
/// The conversion of a value to its attribute's type (FBL 0.5 §5.2, §7.4): the table of §5.2 cell by
/// cell, read through a binding, and the conversions as they are given a value, which keep the cases
/// of the TypedValue tests they replace. A value arrives as its family read it: typed from YAML and
/// JSON, always text from XML, lines and blocks, which convert from that text as before FBL 0.5.
/// </summary>
public class ScalarConversionTests
{
    private const string Unreadable = "(the entry is unreadable)";
    private const string Absent = "(the attribute is absent)";

    private static readonly FblBinding _binding = Load("yaml", """
        {
          "s": { "key": "s" },
          "i": { "key": "i", "default": 0 },
          "n": { "key": "n" },
          "b": { "key": "b" },
          "o": { "key": "o" },
          "ic": { "key": "ic", "quoted": "convert" },
          "nc": { "key": "nc", "quoted": "convert" },
          "bc": { "key": "bc", "quoted": "convert" }
        }
        """);

    private static readonly FblOptions _types = new()
    {
        FileName = "plan.t",
        AttributeType = (_, attribute) => attribute switch
        {
            "s" => "string",
            "i" or "ic" => "int",
            "n" or "nc" => "number",
            "b" or "bc" => "bool",
            "o" => "Colour",
            _ => null,
        },
    };

    // The rows of the table of FBL 0.5 §5.2, one column at a time. The library reads YAML by the 1.2
    // core schema, where yes is a string: a bool attribute written yes does not convert, and an
    // attribute of another type reads it as the text yes (the delivery report names this reading).
    [Theory]
    [InlineData("s", "yes", "yes")]
    [InlineData("s", "1.0", "1.0")]
    [InlineData("s", "007", "007")]
    [InlineData("s", "\"12\"", "12")]
    [InlineData("s", "", "")]
    [InlineData("i", "yes", Unreadable)]
    [InlineData("i", "1.0", 1L)]
    [InlineData("i", "007", 7L)]
    [InlineData("i", "\"12\"", Unreadable)]
    [InlineData("ic", "\"12\"", 12L)]
    [InlineData("ic", "'12'", 12L)]
    [InlineData("i", "1.5", Unreadable)]
    [InlineData("n", "yes", Unreadable)]
    [InlineData("n", "1.0", 1.0)]
    [InlineData("n", "007", 7L)]
    [InlineData("n", "\"12\"", Unreadable)]
    [InlineData("nc", "\"12\"", 12L)]
    [InlineData("nc", "\"twelve\"", Unreadable)]
    [InlineData("b", "yes", Unreadable)]
    [InlineData("b", "true", true)]
    [InlineData("b", "1.0", Unreadable)]
    [InlineData("b", "007", Unreadable)]
    [InlineData("b", "\"true\"", Unreadable)]
    [InlineData("bc", "\"true\"", true)]
    [InlineData("o", "yes", "yes")]
    [InlineData("o", "1.0", 1.0)]
    [InlineData("o", "007", 7L)]
    [InlineData("o", "\"12\"", "12")]
    public void AScalarReadsAsTheTableOfFbl52Says(string attribute, string written, object expected)
    {
        // Act.
        var observed = Observe(Yaml($"items:\n  - id: a\n    {attribute}: {written}\n"), attribute);

        // Assert.
        Assert.Equal(expected, observed);
    }

    // A default or a fallback is read from the binding's JSON as a body's reader reads it: 0 as an int (B-38).
    [Theory]
    [InlineData("i", 0L)]
    [InlineData("n", Absent)]
    [InlineData("b", Absent)]
    [InlineData("o", Absent)]
    public void NothingWrittenIsAbsentAndTheDefaultApplies(string attribute, object expected)
    {
        // Act: the key with no value, and the key not there at all.
        var empty = Observe(Yaml($"items:\n  - id: a\n    {attribute}:\n"), attribute);
        var missing = Observe(Yaml("items:\n  - id: a\n"), attribute);

        // Assert.
        Assert.Equal(expected, empty);
        Assert.Equal(expected, missing);
    }

    // Behaviour change B-38: a default 0 and a written 0 are one CEL type, typed by an attribute or not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADefaultAndAWrittenValueReadAsTheSameCelType(bool typed)
    {
        // Arrange.
        var options = typed ? _types : new FblOptions { FileName = "plan.t" };

        // Act.
        var written = Observe(OpenBody.Open("items:\n  - id: a\n    i: 0\n"u8.ToArray(), _binding, options).Model, "i");
        var defaulted = Observe(OpenBody.Open("items:\n  - id: a\n"u8.ToArray(), _binding, options).Model, "i");

        // Assert.
        Assert.Equal(0L, written);
        Assert.Equal(written?.GetType(), defaulted?.GetType());
        Assert.Equal(written, defaulted);
    }

    [Theory]
    [InlineData("\"12\"", Unreadable)]
    [InlineData("12", 12L)]
    public void AJsonStringDoesNotConvertToANumberEvenWithQuotedConvert(string written, object expected)
    {
        // Arrange.
        var binding = Load("json", """{ "nc": { "key": "nc", "quoted": "convert" } }""");

        // Act.
        var model = OpenBody.Open(Encoding.UTF8.GetBytes($$"""{ "items": [{ "id": "a", "nc": {{written}} }] }"""), binding, _types).Model;

        // Assert.
        Assert.Equal(expected, Observe(model, "nc"));
    }

    [Fact]
    public void AValueThatDoesNotConvertReadsAsItsFallbackAndIsReportedAtItsSpan()
    {
        // Arrange.
        var binding = Load("yaml", """{ "i": { "key": "i", "fallback": { "value": 0 } } }""");
        const string body = "items:\n  - id: a\n    i: wide   # not a row\n";

        // Act.
        var model = OpenBody.Open(Encoding.UTF8.GetBytes(body), binding, _types).Model;

        // Assert: the entry is read, its attribute is the fallback, and one warning says what was written.
        Assert.Equal(0L, model.Find("a")!.Attributes["i"]);
        var finding = Assert.Single(model.Findings);
        Assert.Equal(FindingCodes.UnconvertedValue, finding.Code);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal("'wide' is not a whole number, so I is read as 0. Write a whole number to change it.", finding.Message);
        Assert.Equal(new Dictionary<string, string> { ["attribute"] = "i", ["text"] = "wide" }, finding.Detail);
        Assert.Equal((3, 8, 4), (finding.Location!.Line, finding.Location.Column, finding.Location.Length));
    }

    [Fact]
    public void AFallbackThatDoesNotReportIsReadWithNoFinding()
    {
        // Arrange.
        var binding = Load("yaml", """{ "i": { "key": "i", "fallback": { "value": 0, "report": false } } }""");

        // Act.
        var model = OpenBody.Open("items:\n  - id: a\n    i: wide\n"u8.ToArray(), binding, _types).Model;

        // Assert.
        Assert.Equal(0L, model.Find("a")!.Attributes["i"]);
        Assert.Empty(model.Findings);
    }

    [Fact]
    public void AValueThatDoesNotConvertWithoutAFallbackMakesItsEntryUnreadable()
    {
        // Act.
        var model = Yaml("items:\n  - id: a\n    i: wide\n  - id: b\n    i: 2\n");

        // Assert: the other entry is read as usual.
        Assert.Equal(["b"], model.Elements.Select(e => e.Id));
        var finding = Assert.Single(model.Findings);
        Assert.Equal(FindingCodes.UnreadableEntry, finding.Code);
        Assert.Contains("'i' is not a int.", finding.Message, StringComparison.Ordinal);
    }

    // The cases of the TypedValue tests this class replaces, case for case: text from the xml, lines
    // and blocks families converts as before FBL 0.5, and is kept as it was written.
    [Theory]
    [InlineData("number", "931298")]
    [InlineData("number", "-1.5e3")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-10-09")]
    [InlineData("datetime", "2026-10-09T08:30:00+02:00")]
    [InlineData("time", "08:30")]
    [InlineData("time", "08:30:15")]
    [InlineData("text", "anything")]
    [InlineData("Property", "p1")]
    public void TextThatConverts_Reads(string type, string value)
    {
        // Act.
        var converts = ScalarConversion.Convert(type, value, value, null, false, out var converted);

        // Assert.
        Assert.True(converts);
        Assert.Equal(value, converted);
    }

    [Theory]
    [InlineData("number", "about half a million")]
    [InlineData("number", "NaN")]
    [InlineData("number", "")]
    [InlineData("bool", "yes")]
    [InlineData("date", "9 October")]
    [InlineData("date", "2026-13-01")]
    [InlineData("datetime", "tomorrow")]
    [InlineData("time", "half past eight")]
    public void TextThatDoesNotConvert_DoesNotRead(string type, string value) => Assert.False(ScalarConversion.Convert(type, value, value, null, false, out _));

    [Fact]
    public void TypedValues_ReadAsTheirOwnTypeOnly()
    {
        // Assert: a written scalar of a yaml or json body, in plain style.
        Assert.True(Converts("number", 931298L));
        Assert.True(Converts("number", 0.5));
        Assert.True(Converts("bool", false));
        Assert.False(Converts("number", true));
        Assert.False(Converts("bool", 1L));
        Assert.False(Converts("date", 20261009L));
    }

    private static bool Converts(string type, object value) => ScalarConversion.Convert(type, value, NewText.Plain(value, null), "plain", false, out _);

    private static object? Observe(FblModel model, string attribute) =>
        model.Find("a") is not { } element ? Unreadable : element.Attributes.TryGetValue(attribute, out var value) ? value : Absent;

    private static FblModel Yaml(string body) => OpenBody.Open(Encoding.UTF8.GetBytes(body), _binding, _types).Model;

    private static FblBinding Load(string family, string attributes)
    {
        var json = $$"""{ "fbl": "0.5", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "attributes": {{attributes}} }] } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }
}
