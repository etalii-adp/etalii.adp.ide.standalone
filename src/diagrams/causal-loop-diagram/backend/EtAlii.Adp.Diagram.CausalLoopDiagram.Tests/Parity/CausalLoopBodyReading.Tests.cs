using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Every document of the parity corpus reads through the binding to the variables, links and loops
/// <see cref="CausalLoopParser"/> reads, field for field and line for line, the lenient spellings
/// included (causal-loop-disl-fbl Requirement 4.2, Q2); and the statements the binding does not read
/// are the lines the parser could not read, with the parser's sentence for each (Requirement 4.4).
/// </summary>
public class CausalLoopBodyReadingTests
{
    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task TheBindingReads_WhatTheParserReads(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);
        var parsed = CausalLoopParser.Parse(CausalLoopDocument.Parse(text));

        // Act.
        var body = CausalLoopBody.Parse(text);
        var read = body.Read();

        // Assert.
        AssertSame(relative, Lines(parsed.Model), Lines(read));
        AssertSame(
            relative,
            [.. parsed.Problems.Select(problem => $"line {problem.Lines.Start + 1} | {problem.Message}")],
            [.. body.UnreadableStatements.Select(statement => $"line {statement.Line} | {CausalLoopUnreadable.SentenceFor(statement.Statement)}")]);
    }

    [Fact]
    public void TheCorpus_HasTheLenientSpellingsAndTheUnreadableLines()
    {
        // Assert: the comparison above is not vacuous for the two cases it is for.
        var corpus = CausalLoopTranscript.Corpus();
        Assert.Contains(corpus, path => path.EndsWith("/lenient-spellings.cld", StringComparison.Ordinal));
        Assert.Contains(corpus, path => path.EndsWith("/unreadable-lines.cld", StringComparison.Ordinal));
    }

    private static void AssertSame(string name, IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{name}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");

    private static List<string> Lines(CausalLoopModel model) =>
    [
        .. model.Variables.Select(variable => $"variable {Q(variable.Id)} {Q(variable.Label)} @{variable.Lines.Start}-{variable.Lines.End}"),
        .. model.Links.Select(link => $"link {Q(link.From)} -> {Q(link.To)} {link.Polarity} delayed={link.Delayed} flipped={link.Flipped} weight={link.Weight?.ToString("R", CultureInfo.InvariantCulture) ?? "none"} note={Q(link.Label)} @{link.Lines.Start}-{link.Lines.End}"),
        .. model.Loops.Select(loop => $"loop {Q(loop.Identifier)} {Q(loop.Name)} [{string.Join(", ", loop.Variables.Select(Q))}] @{loop.Lines.Start}-{loop.Lines.End}"),
    ];

    private static string Q(string text) => $"«{text}»";
}
