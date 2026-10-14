using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones (causal-loop-disl-fbl
/// Requirements 3.1 and 3.2), row for row and candidate for candidate: for every document of the parity
/// corpus, every element id, the empty id and an unknown id, readable and read-only.
/// </summary>
/// <remarks>
/// Where the hand-written rows throw - a loop of a document that declares one variable twice (finding A7) -
/// the derived ones are required not to, and to be the definition's form (finding A7, reworded by chat ruling
/// 2026-10-13); that is the one place the two are not compared.
/// </remarks>
public class CausalLoopDerivedRowsTests
{
    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryRow_IsTheHandWrittenOne(string relative)
    {
        // Arrange.
        var readable = await CausalLoopEntries.ReadAsync(relative, TestContext.Current.CancellationToken);
        var readOnly = readable with { Unreadable = "parity: read-only" };

        // Act and assert.
        foreach (var target in CausalLoopEntries.Targets(readable.Model, withGestures: false))
        {
            foreach (var entry in new[] { readable, readOnly })
            {
                var actual = CausalLoopDefinition.Rows(entry, target).Select(Text).ToList();
                List<string> expected;
                try
                {
                    expected = [.. HandWrittenCausalLoop.Rows(entry, target).Select(Text)];
                }
                catch (ArgumentException)
                {
                    // Finding A7: the derived rows were read without throwing, and are the form's.
                    Assert.NotEmpty(actual);
                    continue;
                }

                Assert.True(
                    expected.SequenceEqual(actual),
                    $"{relative} {target} (read-only: {!entry.IsUsable}):\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            }
        }
    }

    [Fact]
    public async Task ALoopOfAVariableDeclaredTwice_HasItsRows_WhereTheHandWrittenOnesThrew()
    {
        // Arrange.
        var entry = await CausalLoopEntries.ReadAsync(
            CausalLoopTranscript.Corpus().Single(path => path.EndsWith("/duplicate-variable.cld", StringComparison.Ordinal)),
            TestContext.Current.CancellationToken);

        // Act.
        var oracle = Record.Exception(() => HandWrittenCausalLoop.Rows(entry, "loop:R1"));
        var rows = CausalLoopDefinition.Rows(entry, "loop:R1");

        // Assert.
        Assert.IsType<ArgumentException>(oracle);
        Assert.Equal(
            [
                "causal-loop.loop-identifier | Identifier | R1 | Line |  | Identity | null",
                "causal-loop.loop-name | Name | Twice declared | Line |  | Identity | null",
                "causal-loop.loop-computed | Computed polarity | reinforcing | Line | Counted from the polarity of the links around the cycle. Change an arrow to change this. | Feedback | null",
                "causal-loop.loop-variables | Runs through | a → b | Choice |  | Feedback | [a → b]",
            ],
            rows.Select(Text));
    }

    private static string Text(ContextPropertyDefinition row) =>
        $"{row.Id} | {row.Label} | {row.Value.Replace("\n", "\\n", StringComparison.Ordinal)} | {row.Editor} | {row.ReadOnlyReason} | {row.Group} | {(row.Candidates is null ? "null" : "[" + string.Join("; ", row.Candidates) + "]")}";
}
