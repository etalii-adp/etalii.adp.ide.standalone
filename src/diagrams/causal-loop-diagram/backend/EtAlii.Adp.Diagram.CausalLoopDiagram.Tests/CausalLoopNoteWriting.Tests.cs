using EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// A link's note is written where the tool reads it from (<see cref="CausalLoopNote"/>): where that is a
/// bare word or the last of two quoted ones rather than the binding's slot, a new note replaces the word
/// the tool read and an empty one removes every word it could read as one, so the note reads back as it
/// was set and nothing else on the line or in the file changes.
/// </summary>
public class CausalLoopNoteWritingTests
{
    private const string Path = "notes.cld";

    public static TheoryData<string, string, string> Links() => new()
    {
        { "lenient-spellings.cld", "a", "indented" },
        { "lenient-spellings.cld", "a", "b" },
        { "lenient-spellings.cld", "my id", "a" },
        { "two-notes.cld", "a", "b" },
        { "two-notes.cld", "b", "a" },
    };

    [Theory]
    [MemberData(nameof(Links))]
    public async Task ANoteSet_ReadsBackAsSet_AndAnEmptyOneAsNone_ChangingOnlyItsLine(string fixture, string from, string to)
    {
        // Arrange.
        var relative = CausalLoopCorpus.Paths().Single(path => path.EndsWith("/" + fixture, StringComparison.Ordinal));
        var original = await File.ReadAllTextAsync(IoPath.Combine(CausalLoopCorpus.SourceFolder, relative), TestContext.Current.CancellationToken);
        var store = new CausalLoopMemoryStore { [Path] = original };
        var handler = new SetLinkLabelCommandHandler(store);

        foreach (var note in new[] { "Guard note", "" })
        {
            var before = store[Path];

            // Act.
            var result = await handler.ExecuteAsync(new SetLinkLabelCommand(Path, from, to, note), TestContext.Current.CancellationToken);

            // Assert: the note reads back as set, and every other reading of the file is as it was.
            Assert.True(result.IsSuccess, result.Error);
            var after = store[Path];
            var link = Assert.Single(CausalLoopBody.Parse(after).Read().Links, candidate => candidate.From == from && candidate.To == to);
            Assert.Equal(note, link.Label);
            Assert.Equal(Without(before, from, to), Without(after, from, to));
            Assert.Equal(OtherLines(before, link.Lines.Start), OtherLines(after, link.Lines.Start));
        }
    }

    /// <summary>Everything the file reads, but the note of the link from <paramref name="from"/> to <paramref name="to"/> and where statements are.</summary>
    private static string Without(string text, string from, string to)
    {
        var model = CausalLoopBody.Parse(text).Read();
        return string.Join(
            "\n",
            model.Variables.Select(variable => $"variable {variable.Id} {variable.Label}")
                .Concat(model.Links.Select(link => $"link {link.From} {link.To} {link.Polarity} {link.Delayed} {link.Flipped} {link.Weight} {(link.From == from && link.To == to ? "" : link.Label)}"))
                .Concat(model.Loops.Select(loop => $"loop {loop.Identifier} {loop.Name} {string.Join(" ", loop.Variables)}")));
    }

    private static IEnumerable<string> OtherLines(string text, int line) =>
        text.Split("\r\n").Where((_, index) => index != line);
}
