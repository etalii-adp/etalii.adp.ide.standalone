using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The document as bytes, which is where the round-trip claim either holds or does not: the dependency-graph corpus held to
/// <see cref="LineDocumentContract{TCorpus}"/>, and what only this type says of its documents.
/// </summary>
public sealed class DependencyGraphDocumentTests : LineDocumentContract<DependencyGraphDocumentTests>, ILineDocumentCorpus
{
    public static string Extension => ".dgr";

    public static int MinimumFixtures => 11;

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void NoFixtureCarriesATime(string fixture)
    {
        // Arrange.
        var text = Read(fixture);

        // Assert.
        // The deletion, asserted rather than assumed. This type is the timeline with time removed,
        // so a begin, an end or a duration reaching the corpus is a fixture that was forked and
        // not finished - and it would be the parser's silent excuse to grow a date reader again.
        foreach (var key in new[] { "begin:", "end:", "duration:" })
        {
            Assert.DoesNotContain(key, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
