using System.Text.Json;
using EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The body read through the binding reads every document of the corpus as the parser it replaced did (wardley-map-disl-fbl
/// task 11): the same statements, in the same order, with the same values, and the same lines passed over in the same
/// sentences. The parser stays in this project as the oracle.
/// </summary>
public class WardleyBodyTests
{
    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDocument_ReadsAsTheParserReadIt(string name)
    {
        // Arrange.
        var document = new WardleyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var text = TranscriptText.Decode(await document.Read(TestContext.Current.CancellationToken));

        // Act.
        var read = WardleyBody.Read(LineDocument.Parse(text));

        // Assert: the map, and the key of every element as the module kept it before the binding gave them (task 12).
        var expected = WardleyParser.Outcome(LineDocument.Parse(text));
        Assert.Equal(JsonSerializer.Serialize(expected.Model with { Holders = read.Model.Holders }), JsonSerializer.Serialize(read.Model));
        Assert.Equal(
            WardleyIdentityKeys.All(expected.Model).Select(Keyed).Order(StringComparer.Ordinal),
            read.Model.Holders.Select(Keyed).Order(StringComparer.Ordinal));
        Assert.Equal(JsonSerializer.Serialize(expected.Problems), JsonSerializer.Serialize(read.Problems));
    }

    private static string Keyed(WardleyHolder holder) => $"{holder.Line}|{holder.Kind}|{holder.Key}";

    /// <summary>
    /// No statement that opens with a keyword is read as a link, even one holding two arrows as <c>y_axis</c> does, because the
    /// binding's <c>keyword</c> rule comes before its link rules.
    /// </summary>
    [Fact]
    public async Task NoKeywordStatement_IsReadAsALink()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unmodelled-statements.owm"), TestContext.Current.CancellationToken);
        var lines = LineDocument.Parse(text).Lines;
        string[] keywords = ["y_axis", "x_axis", "evolution", "presentation", "title", "style", "size", "annotations", "annotation", "evolve", "note", "url", "pioneers", "settlers", "townplanners", "accelerator", "deaccelerator", "pipeline", "component", "anchor", "submap"];
        Assert.Contains(lines, line => line.Text.TrimStart().StartsWith("y_axis", StringComparison.OrdinalIgnoreCase) && line.Text.Contains("->", StringComparison.Ordinal));

        // Act.
        var map = WardleyBody.Read(LineDocument.Parse(text)).Model;

        // Assert.
        Assert.NotEmpty(map.Links);
        Assert.All(map.Links, link => Assert.DoesNotContain(keywords, keyword => lines[(int)link.Line - 1].Text.TrimStart().Split(' ')[0].Equals(keyword, StringComparison.OrdinalIgnoreCase)));
    }
}
