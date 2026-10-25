using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The CEL functions the definition takes from the module (wardley-map-disl-fbl task 6): <c>fixedNumber</c> writes a
/// number as the format does whatever the user's culture, <c>positions</c> reads every annotation of the corpus as the
/// parser does, and <c>writtenDecorators</c> gives an element's decorators in the order its statement writes them.
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
    public async Task Positions_ReadsEveryAnnotationAsTheParserDoes(string name)
    {
        // Arrange.
        var document = new WardleyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var lines = LineBreak().Split(TranscriptText.Decode(await document.Read(TestContext.Current.CancellationToken)));
        var map = WardleyParser.Parse(LineDocument.Parse(TranscriptText.Decode(await document.Read(TestContext.Current.CancellationToken))));

        // Act and assert.
        foreach (var annotation in map.Annotations)
        {
            List<object?> expected = [.. annotation.Occurrences.Select(occurrence => (object?)new List<object?> { occurrence.Visibility, occurrence.Maturity })];
            var written = Bracketed(lines[annotation.Line - 1]);

            Assert.Equal(expected, WardleyFunctions.Positions(written), PairComparer.Instance);
            Assert.Equal(expected, WardleyFunctions.Positions(WardleyFunctions.PositionsText(annotation.Occurrences)), PairComparer.Instance);
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
        var diagram = WardleyDisl.Of(entry.Document, entry.Model, session.Store.Identities(session.Body));

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

    /// <summary>The text between the outer brackets of an <c>annotation</c> statement.</summary>
    private static string Bracketed(string line)
    {
        var start = line.IndexOf('[', StringComparison.Ordinal);
        var depth = 0;
        for (var index = start; index < line.Length; index++)
        {
            depth += line[index] switch { '[' => 1, ']' => -1, _ => 0 };
            if (depth == 0) return line[(start + 1)..index];
        }

        throw new InvalidOperationException($"'{line}' has no closing bracket.");
    }

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"\((?<words>[^()]*)\)")]
    private static partial Regex Decorated();

    private sealed class PairComparer : IEqualityComparer<object?>
    {
        public static PairComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => x is List<object?> left && y is List<object?> right && left.SequenceEqual(right);

        public int GetHashCode(object? obj) => 0;
    }
}
