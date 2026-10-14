using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The stand-in that words the refusal of a body that is not YAML (timeline-disl-fbl Requirement
/// 4.2): YamlDotNet's sentence and line for the two documents of the corpus it refuses, and nothing
/// for every other one.
/// </summary>
public sealed class TimelineYamlSyntaxTests
{
    /// <summary>The documents YamlDotNet refuses, with the sentence and the line the transcript records for each.</summary>
    private static readonly Dictionary<string, (string Message, int Line)> Refused = new(StringComparer.Ordinal)
    {
        ["findings/unparseable.tml"] = ("While scanning a plain scalar value, found invalid mapping.", 4),
        ["findings/duplicate-key.tml"] = ("Duplicate key label", 6),
    };

    /// <summary>Every fixture, as a path relative to the fixtures folder.</summary>
    public static TheoryData<string> Fixtures() => new(FixtureNames());

    private static List<string> FixtureNames() =>
    [
        .. Directory.GetFiles(FixturesFolder, "*.tml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(FixturesFolder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    private static string FixturesFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task AFixture_IsRefusedWithTodaysSentenceAndLine_OnlyWhenItIsNotYaml(string fixture)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(FixturesFolder, fixture), TestContext.Current.CancellationToken);

        // Act.
        var refusal = TimelineYamlSyntax.Check(text);
        (string Message, int Line)? expected = Refused.TryGetValue(fixture, out var known) ? known : null;

        // Assert.
        Assert.Equal(expected, refusal);
    }

    [Fact]
    public void TheCorpus_HoldsBothRefusedDocuments()
    {
        // Act.
        var fixtures = FixtureNames();

        // Assert.
        Assert.All(Refused.Keys, fixture => Assert.Contains(fixture, fixtures));
    }
}
