using System.Text.Json;
using System.Text.Json.Serialization;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The parser's statement table, in which order does not matter (code-structure T236): a statement
/// is found by its whole first word, so `annotations` is never read as `annotation`, wherever the
/// two stand in the table.
/// </summary>
public class WardleyParserStatementsTests
{
    private static string FixturesPath => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Every statement of the table, read and unread, the two keywords one of which is a prefix of
    /// the other, a keyword in capitals and after a tab, and the lines that are no statement.
    /// </summary>
    private const string EveryStatement = """
        title Tea Shop
        style wardley
        size [1100, 800]
        size nonsense
        annotations [0.6, 0.02]
        annotations nowhere
        ANNOTATIONS [0.5, 0.03]
        annotation 1 [0.3, 0.4] first
        annotation 2 [[0.1,0.2],[0.3,0.4]] two places
        annotation x
        evolve Kettle 0.6
        evolve Kettle->Electric Kettle 0.7
        evolve
        note A note [0.2, 0.3]
        note [0.2, 0.3]
        url foo [https://example.org/x]
        url bad
        pioneers [0.1, 0.2, 0.3, 0.4]
        SETTLERS [0.1, 0.2, 0.3, 0.4]
        townplanners [0.1, 0.2]
        accelerator Fast [0.5, 0.5]
        accelerator
        deaccelerator Slow [0.5, 0.6]
        deaccelerator nowhere
        component Cup [0.79, 0.61] label [-19, -4] (build) inertia url(foo)
        anchor Business [0.95, 0.63]
        submap Kitchen [0.5, 0.5] url(k)
        component NoPlace
        Component	Tabbed [0.2, 0.3]
        anchor
        Cup->Kettle
        Cup+>Water; flows
        pipeline Kettle
        {
          component Electric [0.4]
          component Broken
          something else
        }
        pipeline Legacy [0.3, 0.7]
        pipeline
        y_axis Value chain->Invisible->Visible
        titles are not titles
        annotationsx [0.6, 0.02]
        notes [0.1, 0.1]
        random words here
        pipeline Unclosed {
          component Kid [0.5]
        """;

    /// <summary>The documents read under every order: the fixtures, and <see cref="EveryStatement"/>.</summary>
    private static IEnumerable<(string Name, LineDocument Document)> Documents()
    {
        yield return ("every statement", LineDocument.Parse(EveryStatement));
        foreach (var path in Directory.EnumerateFiles(FixturesPath, "*.owm").Order(StringComparer.Ordinal))
        {
            yield return (IoPath.GetFileName(path), LineDocument.Parse(File.ReadAllText(path)));
        }
    }

    public static TheoryData<string> Orders => new("reversed", "seed 1", "seed 2", "seed 3", "seed 4", "seed 5", "seed 6", "seed 7", "seed 8");

    private static WardleyParser.Statement[] Ordered(string order)
    {
        if (order == "reversed")
        {
            return WardleyParser.Statements.Reverse().ToArray();
        }

        var random = new Random(int.Parse(order["seed ".Length..], System.Globalization.CultureInfo.InvariantCulture));
        var shuffled = WardleyParser.Statements.ToArray();
        random.Shuffle(shuffled);
        return shuffled;
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void Outcome_IsTheSame_WhateverTheOrderOfTheStatementTable(string order)
    {
        // Arrange.
        var statements = Ordered(order);

        foreach ((string name, LineDocument document) in Documents())
        {
            // Act.
            var expected = JsonSerializer.Serialize(WardleyParser.Outcome(document), _options);
            var actual = JsonSerializer.Serialize(WardleyParser.Outcome(document, statements), _options);

            // Assert. The model and the problems, every field, in the same order.
            Assert.True(expected == actual, $"{name} reads differently with the table in order {order}:\n{actual}");
        }
    }

    [Fact]
    public void Statements_PutAnnotationsBeforeAnnotation_SoThatTheReversedOrderPutsItAfter()
    {
        // Act.
        var keywords = WardleyParser.Statements.Select(statement => statement.Keyword).ToList();

        // Assert. The reversed order of the theory above is the one that puts the prefix first.
        Assert.True(keywords.IndexOf("annotations") < keywords.IndexOf("annotation"));
    }

    [Fact]
    public void Statements_HaveOneWordKeywords_NoTwoAlike()
    {
        // Act.
        var keywords = WardleyParser.Statements.Select(statement => statement.Keyword).ToArray();

        // Assert. Two entries with one keyword would make the first of them win, which is order.
        Assert.Equal(keywords.Length, keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keywords, keyword => Assert.DoesNotContain(keyword, char.IsWhiteSpace));
    }
}
