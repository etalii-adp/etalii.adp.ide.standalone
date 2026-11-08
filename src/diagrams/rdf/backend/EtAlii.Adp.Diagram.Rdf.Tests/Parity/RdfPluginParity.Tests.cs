using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The plugin reads what the parser reads (rdf-disl-fbl task 7, Requirement 3): for every document of the corpus,
/// its <c>Triple</c>, <c>Prefix</c> and <c>Base</c> elements are the parser's triples, prefixes and base, in order;
/// its findings are the five <c>rdf.*</c> findings <see cref="RdfValidator"/> gives; a body that does not parse is
/// unreadable exactly where the store holds it so; two reads of the same bytes are equal; and no two elements claim
/// one span, each value span lying in its element's and spelling its value.
/// </summary>
public class RdfPluginParityTests
{
    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (var body in RdfCorpus.Bodies())
        {
            data.Add(body);
        }

        return data;
    }

    private static byte[] BytesOf(string relative) => File.ReadAllBytes(Path.Combine(RdfCorpus.SourceFolder, relative));

    /// <summary>The text of a document as the store reads it: its byte-order mark, if it has one, not part of it.</summary>
    private static string TextOf(byte[] bytes) => TranscriptText.Decode(bytes).TrimStart('\uFEFF');

    [Theory]
    [MemberData(nameof(Documents))]
    public void ThePluginsElements_AreTheParsersTriplesPrefixesAndBase_InOrder(string relative)
    {
        // Arrange.
        var bytes = BytesOf(relative);
        var outcome = RdfParser.Outcome(LineDocument.Parse(TextOf(bytes)));

        // Act.
        var reading = RdfBody.Open(bytes, relative).Reading;

        // Assert.
        Assert.Equal(!outcome.IsUsable, reading.Unreadable);
        var model = outcome.Model;
        Assert.Equal(model.Triples.Select(Expected), reading.Elements.Where(element => element.Type == "Triple").Select(Actual));
        Assert.Equal(model.Prefixes.Select(prefix => $"{prefix.Prefix} {prefix.Iri} line {prefix.Line + 1}"),
            reading.Elements.Where(element => element.Type == "Prefix").Select(element => $"{element.Attributes["name"]} {element.Attributes["iri"]} line {element.Attributes["line"]}"));
        Assert.Equal(model.BaseIri is null ? [] : [$"{model.BaseIri} line {model.BaseLine + 1}"],
            reading.Elements.Where(element => element.Type == "Base").Select(element => $"{element.Attributes["iri"]} line {element.Line}"));
        Assert.Equal(model.Triples.Count + model.Prefixes.Count + (model.BaseIri is null ? 0 : 1), reading.Elements.Count);
        Assert.Equal(reading.Elements.OrderBy(element => element.OwnSpan.Start), reading.Elements);
        Assert.All(reading.Elements, element => Assert.False(element.IdIsStored));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task TheFindings_AreTheValidatorsFiveRdfFindings_AtTheirCodeSeveritySentenceAndLine(string relative)
    {
        // Arrange.
        var bytes = BytesOf(relative);
        var problems = await new RdfValidator(ServiceCollectionAddRdfExtension.RdfOrigin).ValidateAsync(
            new DiagramValidationRequest(TextOf(bytes), Path.GetFileNameWithoutExtension(relative), ProjectRoot.From(RdfCorpus.SourceFolder), Path.Combine(RdfCorpus.SourceFolder, relative), null),
            TestContext.Current.CancellationToken);

        // Act.
        var findings = RdfBody.Open(bytes, relative).Reading.Findings;

        // Assert.
        Assert.Equal(
            problems.Select(problem => $"{problem.RuleId} | {problem.Severity} | {problem.Message} | line {((DiagramProblemLineLocation)problem.Location!).Number}"),
            findings.Select(finding => $"{finding.Code} | {finding.Severity} | {finding.Message} | line {finding.Location!.Line}"));
        Assert.All(findings, finding => Assert.StartsWith("rdf.", finding.Code, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void TwoReadsOfTheSameBytes_AreEqual(string relative)
    {
        // Arrange.
        var bytes = BytesOf(relative);

        // Act.
        var first = RdfBody.Open(bytes, relative).Reading;
        var second = RdfBody.Open([.. bytes], relative).Reading;

        // Assert.
        Assert.Equal(Describe(first), Describe(second));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void NoSpanIsClaimedTwice_AndEveryValueSpanSpellsItsValueInsideItsElement(string relative)
    {
        // Arrange.
        var bytes = BytesOf(relative);

        // Act.
        var reading = RdfBody.Open(bytes, relative).Reading;
        var elements = reading.Elements;

        // Assert.
        var claimed = elements.SelectMany(element => element.ValueSpans.Select(value => (Element: element, Name: value.Key, Span: value.Value))).OrderBy(claim => claim.Span.Start).ToList();
        for (var index = 1; index < claimed.Count; index++)
        {
            Assert.True(claimed[index].Span.Start >= claimed[index - 1].Span.End, $"{claimed[index - 1].Name} of {claimed[index - 1].Element.Id} and {claimed[index].Name} of {claimed[index].Element.Id} both claim bytes {claimed[index].Span.Start}..{claimed[index - 1].Span.End}.");
        }

        foreach ((FblElement element, string name, Span span) in claimed)
        {
            Assert.True(span.Start >= element.OwnSpan.Start && span.End <= element.OwnSpan.End, $"{name} of {element.Id} lies outside it.");
            var written = Encoding.UTF8.GetString(bytes, span.Start, span.End - span.Start);
            var value = (string)element.Attributes[name]!;
            Assert.True(name switch
            {
                "o" when (string)element.Attributes["oKind"]! == "literal" => written.Length > 0,
                "o" or "s" when written.StartsWith("_:", StringComparison.Ordinal) => true,
                "language" => written == value,
                _ => written.StartsWith('<') || written == "a" || written.Contains(':', StringComparison.Ordinal),
            }, $"{name} of {element.Id} is written '{written}' for '{value}'.");
        }

        // Every document that parses writes an object of its own somewhere.
        Assert.True(reading.Unreadable || claimed.Any(claim => claim.Name == "o"), "No object has a span of its own.");
    }

    /// <summary>A parser's triple, in the shape the plugin's element is described.</summary>
    private static string Expected(RdfTriple triple)
    {
        var literal = triple.Object as LiteralTerm;
        return string.Join(" | ",
            Term(triple.Subject), Kind(triple.Subject), (triple.Subject as BlankTerm)?.Label ?? "",
            triple.Predicate.Iri,
            Term(triple.Object), Kind(triple.Object), (triple.Object as BlankTerm)?.Label ?? "",
            literal?.Language ?? "", literal?.DatatypeIri ?? "",
            (triple.Span.StartLine + 1).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A plugin's <c>Triple</c> element, described.</summary>
    private static string Actual(FblElement element) => string.Join(" | ",
        element.Attributes["s"], element.Attributes["sKind"], element.Attributes["sLabel"],
        element.Attributes["p"],
        element.Attributes["o"], element.Attributes["oKind"], element.Attributes["oLabel"],
        element.Attributes["language"], element.Attributes["datatype"],
        element.Attributes["line"]);

    private static string Term(RdfTerm term) => term switch
    {
        IriTerm iri => iri.Iri,
        BlankTerm blank => blank.Ordinal.ToString(CultureInfo.InvariantCulture),
        LiteralTerm literal => literal.Lexical,
        _ => "?",
    };

    private static string Kind(RdfTerm term) => term switch
    {
        IriTerm => "iri",
        BlankTerm => "blank",
        LiteralTerm => "literal",
        _ => "?",
    };

    /// <summary>Everything a reading delivers, as text.</summary>
    private static string Describe(FblModel reading) => string.Join("\n",
        reading.Elements.Select(element => $"{element.Id} {element.Type} {element.OwnSpan} line {element.Line} "
            + string.Join(",", element.Attributes.Select(attribute => $"{attribute.Key}={attribute.Value}"))
            + " " + string.Join(",", element.ValueSpans.Select(value => $"{value.Key}={value.Value}")))
        .Concat(reading.Findings.Select(finding => finding.ToString()))
        .Append($"unreadable {reading.Unreadable}"));
}
