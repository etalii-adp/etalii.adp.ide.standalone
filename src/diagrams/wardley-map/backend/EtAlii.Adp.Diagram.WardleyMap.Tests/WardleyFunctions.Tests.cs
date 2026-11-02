using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The CEL functions the definition takes from the module (wardley-map-disl-fbl task 6): <c>fixedNumber</c> writes a
/// number as the format does whatever the user's culture, an annotation's <c>positions</c> are read for every annotation of the
/// corpus as the parser read them, and <c>writtenDecorators</c> gives an element's decorators in the order its statement writes them.
/// </summary>
public partial class WardleyFunctionsTests
{
    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Theory]
    [InlineData(0.5, "0.5")]
    [InlineData(1.23456, "1.2346")]
    [InlineData(2.0, "2")]
    [InlineData(-85.48, "-85.48")]
    [InlineData(1234567.0, "1234567")]
    public void FixedNumber_WritesTheInvariantForm_UnderACultureWithADecimalComma(double value, string expected)
    {
        // Arrange.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            // The culture is one a culture-following format would get wrong.
            Assert.Equal("0,5", 0.5.ToString(CultureInfo.CurrentCulture));

            // Act.
            var written = WardleyFunctions.FixedNumber(value);

            // Assert.
            Assert.Equal(expected, written);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FixedNumber_WritesAWholeNumber_AsItself() => Assert.Equal("3", WardleyFunctions.FixedNumber(3L));

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Positions_ReadEveryAnnotationAsTheParserDoes(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
        var entry = session.Store.Get(session.Body)!;
        var map = WardleyParser.Parse(entry.Document);

        // Act.
        var diagram = WardleyBody.Model(entry.Document, session.Store.Identities(session.Body));

        // Assert: each annotation's positions, by its line, are the parser's occurrences in their order.
        var annotations = diagram.Elements.Where(element => element.IsA("Annotation")).ToList();
        Assert.Equal(map.Annotations.Count, annotations.Count);
        foreach (var annotation in map.Annotations)
        {
            var read = annotations.Single(element => element.Line == annotation.Line);
            List<object?> expected = [.. annotation.Occurrences.Select(occurrence => (object?)new List<object?> { occurrence.Visibility, occurrence.Maturity })];
            List<object?> positions = [.. ((IEnumerable<object?>)read.ValueOf("positions")!).Select(position => (object?)new List<object?> { ((IReadOnlyDictionary<string, object?>)position!)["visibility"], ((IReadOnlyDictionary<string, object?>)position!)["maturity"] })];
            Assert.Equal(Text(expected), Text(positions));
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task WrittenDecorators_AreInTheOrderTheStatementWritesThem(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
        var entry = session.Store.Get(session.Body)!;
        var lines = LineBreak().Split(entry.Document.Text);
        var diagram = WardleyBody.Model(entry.Document, session.Store.Identities(session.Body));

        // Act and assert.
        foreach (var element in diagram.Elements.Where(element => element.IsA("Positioned")))
        {
            var component = entry.Model.Components.Single(candidate => candidate.Name == element.ValueOf("name") as string && WrittenOn(candidate, element));
            // What follows '//' is a comment, which no name can hold.
            var statement = lines[component.Line - 1].Split("//")[0];
            var written = string.Join(", ", Decorated().Matches(statement)
                .SelectMany(match => match.Groups["words"].Value.Split(',').Select(word => word.Trim().ToLowerInvariant()))
                .Where(word => WardleyDecorators.All.Any(decorator => WardleyDecorators.Spell(decorator) == word))
                .Distinct(StringComparer.Ordinal));
            var actual = WardleyFunctions.WrittenDecoratorsOf(element);
            Assert.True(written == actual, $"{name} line {component.Line} ({lines[component.Line - 1]}): written '{written}', read '{actual}'");
        }
    }

    [Fact]
    public void WrittenDecorators_RefuseWhatIsNotAnElement() =>
        Assert.Throws<Specification.Cel.CelException>(() => WardleyFunctions.WrittenDecoratorsOf("market"));

    /// <summary>Whether <paramref name="component"/> is the statement <paramref name="element"/> was read from: the one on its line.</summary>
    private static bool WrittenOn(WardleyComponent component, DislElement element) => element.Line is not { } line || line == component.Line;

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"\((?<words>[^()]*)\)")]
    private static partial Regex Decorated();

    /// <summary>Positions as text, each pair at full precision, so two readings compare by value.</summary>
    private static string Text(List<object?> positions) =>
        string.Join("; ", positions.Select(pair => pair is List<object?> numbers
            ? string.Join(", ", numbers.Select(number => Convert.ToString(number, CultureInfo.InvariantCulture)))
            : $"not a pair: {pair}"));
}
