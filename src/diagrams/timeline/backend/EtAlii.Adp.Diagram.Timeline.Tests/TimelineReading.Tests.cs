using EtAlii.Adp.Diagram.Timeline.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The binding's reading, projected onto <see cref="TimelineModel"/>, against the module's own
/// reader it replaced (<see cref="TimelineParser"/>, the oracle): every document of the parity
/// transcript's corpus gives the model the parser gave, field by field, line ranges included. This
/// is what says the mapper's, the session's, the scale's and the arrangement's input did not change
/// (timeline-disl-fbl Requirement 2), and it names the element where it does.
/// </summary>
public sealed class TimelineReadingTests
{
    /// <summary>Every <c>.tml</c> of the corpus, as a path relative to <c>src/</c>.</summary>
    public static TheoryData<string> Corpus()
    {
        var module = RepositoryPaths.Under("src", "diagrams", "timeline");
        var source = Path.GetFullPath(Path.Combine(module, "..", ".."));
        var fixtures = Path.Combine(module, "backend", "EtAlii.Adp.Diagram.Timeline.Tests", "Fixtures");
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(fixtures, "*.tml", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(Path.Combine(module, "examples"), "*.tml", SearchOption.AllDirectories))
                     .Concat(Directory.GetFiles(Path.Combine(source, "examples", "diagrams", "timeline"), "*.tml", SearchOption.AllDirectories))
                     .Select(path => Path.GetRelativePath(source, path).Replace('\\', '/'))
                     .Order(StringComparer.Ordinal))
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EveryDocument_ReadsAsTheParserReadIt(string document)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(RepositoryPaths.Under(["src", .. document.Split('/')]), TestContext.Current.CancellationToken);
        if (TimelineYamlSyntax.Check(text) is not null)
        {
            // Not YAML: neither reads it, and TimelineYamlSyntaxTests holds what is said instead.
            return;
        }

        var expected = TimelineParser.Parse(LineDocument.Parse(text));

        // Act.
        var read = TimelineReading.Of(text);

        // Assert.
        Assert.Equal(expected.Elements.Count, read.Elements.Count);
        for (var index = 0; index < expected.Elements.Count; index++)
        {
            Assert.True(expected.Elements[index] == read.Elements[index], $"element {index} ({expected.Elements[index].Id}): the parser reads {expected.Elements[index]}, the binding {read.Elements[index]}");
        }

        Assert.Equal(expected.Connections.Count, read.Connections.Count);
        for (var index = 0; index < expected.Connections.Count; index++)
        {
            Assert.True(expected.Connections[index] == read.Connections[index], $"relation {index} ({expected.Connections[index].Id}): the parser reads {expected.Connections[index]}, the binding {read.Connections[index]}");
        }
    }

    [Fact]
    public void WrittenText_ReadsEveryValueAsItWasWritten()
    {
        // Arrange.
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "written-text.tml"));

        // Act.
        var read = TimelineReading.Of(text);

        // Assert.
        Assert.Equal(["007", "alpha", "beta", "gamma"], read.Elements.Select(element => element.Id));
        Assert.Equal(["yes", "1.0", "null", "~"], read.Elements.Select(element => element.Label));
        Assert.Equal([("007", "alpha"), ("7", "beta")], read.Connections.Select(connection => (connection.From, connection.To)));
    }
}
