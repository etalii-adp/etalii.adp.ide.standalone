using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The edits written by splices write what the hand-written ones wrote (functional-decomposition-graph-disl-fbl
/// task 10): for every readable document of the parity corpus, the nine commands on a sample of its elements and
/// connections, and on ids that are not there, give a body that reads the same, or the same refusal, through
/// today's handlers and through <see cref="HandWrittenFdgEdits"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Compared by reading, not by bytes</b>, unlike the hype cycle's <c>GhgDerivedOperationsTests</c>: the binding
/// writes a new key where its <c>keys</c> order puts it, a one-line value plain and only the values that changed
/// (Q1), so the bytes differ by design. Both bodies are read by the oracle's <see cref="FdgParser"/>, and what it
/// reads - every entry's values and every problem's message - must be the same. The transcript holds the bytes. For the same
/// reason every edit here changes each value it names: a value an edit leaves as it is keeps its written form,
/// where the hand-written writer wrote it again in its own (Q1).
/// </para>
/// <para>
/// <b>What the hand-written writer got wrong is listed, not compared</b> (<see cref="Repaired"/>): an edit whose
/// hand-written body the oracle itself cannot read back as written, findings B13 and B14, which this task repairs.
/// </para>
/// </remarks>
public class FdgDerivedOperationsTests
{
    private const string Body = "parity.fdg";
    private const string NewId = "fdgnew";
    private const int Sample = 3;

    public static TheoryData<string> Documents() =>
    [
        .. FdgTranscript.Corpus().Where(relative => !FdgBody.Parse(Text(relative)).IsUnreadable),
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEdit_ReadsAsTheHandWrittenEdit(string relative)
    {
        var text = Text(relative);
        var model = FdgParser.Parse(LineDocument.Parse(text));
        List<string> mismatches = [];
        var compared = 0;

        foreach (var command in Commands(model))
        {
            compared++;
            var (saved, refusal) = await FdgEditing.RunAsync(text, command, TestContext.Current.CancellationToken);
            var actual = saved is null ? $"refused: {refusal}" : Reading(saved);

            var document = LineDocument.Parse(text);
            var outcome = HandWrittenFdgEdits.Apply(document, command);
            var expected = !outcome.WasApplied ? $"refused: {outcome.Refusal}" : Reading(document.Text);

            if (string.Equals(expected, actual, StringComparison.Ordinal)) continue;
            if (Repaired(text, command, document.Text) is { } finding)
            {
                // The hand-written body is not what the command asked for; the derived one must be.
                Assert.True(saved is not null, $"{relative}: {Describe(command)} ({finding}) was refused: {refusal}");
                continue;
            }

            mismatches.Add($"{Describe(command)}\n  hand-written {expected.Replace("\n", "\n               ", StringComparison.Ordinal)}\n  derived      {actual.Replace("\n", "\n               ", StringComparison.Ordinal)}");
        }

        Assert.True(compared > 0, $"{relative}: no edit was compared");
        Assert.True(mismatches.Count == 0, $"{relative}: {mismatches.Count} of {compared} differ\n{string.Join("\n", mismatches)}");
    }

    /// <summary>
    /// The finding an edit's hand-written body shows, when the oracle cannot read it back as the command wrote it:
    /// a Comment's text that its literal block does not hold, a first line that begins with a space or an empty
    /// text (B13), or a first entry added below an empty flow list that carries a comment (B14). Null for every other edit, which is compared.
    /// </summary>
    private static string? Repaired(string text, ICommand command, string handWritten)
    {
        var read = FdgParser.Parse(LineDocument.Parse(handWritten));
        var before = FdgParser.Parse(LineDocument.Parse(text));
        return command switch
        {
            RenameFdgElementCommand rename when before.ElementOf(rename.ElementId) is { IsComment: true } && read.ElementOf(rename.ElementId)?.Text != rename.Value => "B13",
            AddFdgElementCommand or ConnectFdgElementsCommand when read.Problems.Count > before.Problems.Count => "B14",
            _ => null,
        };
    }

    /// <summary>The commands tried on <paramref name="model"/>: adds and connects, the edits of a sample of its entries, and ids that are not there.</summary>
    private static IEnumerable<ICommand> Commands(FdgModel model)
    {
        foreach (var type in FdgElementTypes.All.Append("nothing"))
        {
            yield return new AddFdgElementCommand(Body, type, 240, 160, NewId);
            yield return new AddFdgElementCommand(Body, type, -17.5, 3.25, NewId);
        }

        var elements = model.Elements.Where(element => element.Id.Length > 0)
            .Where((element, index) => index < Sample || element.IsComment)
            .DistinctBy(element => element.Id, StringComparer.Ordinal)
            .Take(Sample * 2)
            .ToList();
        foreach (var element in elements)
        {
            yield return new SetFdgPlacementCommand(Body, element.Id, 300.25, -4E-05);
            yield return new SetFdgPlacementCommand(Body, element.Id, element.X + 1, element.Y + 12);
            yield return new SetFdgSizeCommand(Body, element.Id, 10);
            yield return new SetFdgSizeCommand(Body, element.Id, 200.00005);
            yield return new SetFdgSizeCommand(Body, element.Id, 300, 200);
            yield return new SetFdgSizeCommand(Body, element.Id, 500, 30);
            yield return new RenameFdgElementCommand(Body, element.Id, "Renamed");
            yield return new RenameFdgElementCommand(Body, element.Id, " leading space");
            yield return new RenameFdgElementCommand(Body, element.Id, "Two\nlines");
            yield return new RenameFdgElementCommand(Body, element.Id, "");
            yield return new SetFdgDescriptionCommand(Body, element.Id, "Described: here # and there");
            yield return new SetFdgDescriptionCommand(Body, element.Id, "Two\nlines");
            yield return new SetFdgDescriptionCommand(Body, element.Id, "  ");
            yield return new SetFdgDescriptionCommand(Body, element.Id, "");
            yield return new RemoveFdgElementCommand(Body, element.Id);
        }

        foreach (var connection in model.Connections.Where(connection => connection.Id.Length > 0).DistinctBy(connection => connection.Id, StringComparer.Ordinal).Take(Sample))
        {
            yield return new RenameFdgConnectionCommand(Body, connection.Id, "Renamed link");
            yield return new RenameFdgConnectionCommand(Body, connection.Id, "");
            yield return new SetFdgDescriptionCommand(Body, connection.Id, "A link's description");
            yield return new SetFdgDescriptionCommand(Body, connection.Id, "");
            yield return new DisconnectFdgConnectionCommand(Body, connection.Id);
        }

        var ends = elements.Take(Sample).Select(element => element.Id).Append("parity:unknown").ToList();
        foreach (var type in FdgConnectionTypes.All.Append("nothing"))
        {
            foreach (var from in ends)
            {
                foreach (var to in ends)
                {
                    yield return new ConnectFdgElementsCommand(Body, type, from, to, NewId);
                }
            }
        }

        const string unknown = "parity:unknown";
        yield return new SetFdgPlacementCommand(Body, unknown, 1, 2);
        yield return new SetFdgSizeCommand(Body, unknown, 100);
        yield return new RenameFdgElementCommand(Body, unknown, "x");
        yield return new SetFdgDescriptionCommand(Body, unknown, "x");
        yield return new RemoveFdgElementCommand(Body, unknown);
        yield return new RenameFdgConnectionCommand(Body, unknown, "x");
        yield return new DisconnectFdgConnectionCommand(Body, unknown);
    }

    /// <summary>What the oracle reads of <paramref name="text"/>: every entry's values, in order, and every problem's message.</summary>
    private static string Reading(string text)
    {
        var model = FdgParser.Parse(LineDocument.Parse(text));
        return string.Join(
            "\n",
            model.Elements.Select(element => string.Create(CultureInfo.InvariantCulture, $"element {element.Id} {element.Type} name={element.Name} description={element.Description} text={element.Text} x={Number(element.X)} y={Number(element.Y)} width={Number(element.Width)} height={(element.Height is { } height ? Number(height) : null)}"))
                .Concat(model.Connections.Select(connection => $"connection {connection.Id} {connection.Type} {connection.From} -> {connection.To} name={connection.Name} description={connection.Description}"))
                .Concat(model.Problems.Select(problem => $"problem {problem.Message}")));
    }

    /// <summary>
    /// <paramref name="value"/> with a negative zero made zero: the hand-written writer wrote a value that rounds to zero from
    /// below as <c>-0</c>, the binding writes <c>0</c>, and both are the number zero (finding B19).
    /// </summary>
    private static double Number(double value) => value + 0.0;

    private static string Describe(ICommand command) => command.ToString() ?? command.GetType().Name;

    private static string Text(string relative) => File.ReadAllText(Path.Combine(FdgTranscript.SourceFolder, relative));
}
