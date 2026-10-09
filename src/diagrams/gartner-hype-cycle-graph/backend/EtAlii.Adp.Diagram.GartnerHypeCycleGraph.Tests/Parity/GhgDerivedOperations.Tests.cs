using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The edits run from the DISL definition write exactly what the hand-written ones wrote (runtime
/// plan step S13): for every document of the parity corpus, the three adds at a range of points,
/// Even phases and Remove on every element, and a span change on every trend give byte-identical
/// bodies, or the same refusal, through today's handlers and through <see cref="HandWrittenGhgEdits"/>.
/// </summary>
/// <remarks>
/// <b>Long documents are sampled and the longest left out</b>, as in <see cref="GhgDerivedGesturesTests"/>:
/// every handler call reads the whole document again.
/// </remarks>
public class GhgDerivedOperationsTests
{
    private const string Body = "parity.ghg";
    private const string NewId = "s13new";
    private const int Sample = 3;
    private const int MaxLines = 1000;

    /// <summary>Points on the canvas: on and either side of step and row edges, and on a row's half-way line.</summary>
    private static readonly (double X, double Y)[] Points =
    [
        (0, 0), (3.9, 16), (4, 44), (10, 43.9), (123.5, 100), (-40, -30), (1000, 15.99), (2, 28), (-0.5, 72), (6.25, -16),
    ];

    public static TheoryData<string> Documents() => [.. Edited().Select(document => document.Path)];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEdit_WritesTheHandWrittenBytes(string path)
    {
        var text = Edited().Single(document => document.Path == path).Text;
        var model = GhgParser.Parse(text);
        List<string> mismatches = [];
        List<string> compared = [];

        foreach ((double x, double y) in Points)
        {
            await Compare($"add trend at {x}, {y}", store => new AddGhgTrendCommandHandler(store), new AddGhgTrendCommand(Body, x, y, NewId), (document, read) => HandWrittenGhgEdits.AddTrendAt(document, read, NewId, x, y));
            await Compare($"add trigger at {x}, {y}", store => new AddGhgTriggerCommandHandler(store), new AddGhgTriggerCommand(Body, x, y, NewId), (document, read) => HandWrittenGhgEdits.AddTriggerAt(document, read, NewId, x, y));
            await Compare($"add note at {x}, {y}", store => new AddGhgNoteCommandHandler(store), new AddGhgNoteCommand(Body, x, y, NewId), (document, read) => HandWrittenGhgEdits.AddNoteAt(document, read, NewId, x, y));
        }

        foreach (var trend in Sampled(model.Trends, trend => trend.DraggedEnds.Any(boundary => boundary is not null)))
        {
            await Compare($"even phases of {trend.Id}", store => new ClearGhgBoundariesCommandHandler(store), new ClearGhgBoundariesCommand(Body, trend.Id), (document, read) => HandWrittenGhgEdits.ClearBoundaries(document, read, trend.Id));
            if (!trend.HasSpan) continue;

            (int start, int stop) = (trend.Start!.Value, trend.Stop!.Value);
            foreach ((int from, int to) in new[] { (start - 7, stop - 7), (start + 5, stop + 5), (start, stop + 13), (start - 11, stop), (start + 1, stop + 37), (start - 30, stop - 3) })
            {
                if (to - from < Math.Max(1, trend.VisiblePhases)) continue;
                await Compare(
                    $"span of {trend.Id} to {GhgScale.FormatMonth(from)}..{GhgScale.FormatMonth(to)}",
                    store => new SetGhgSpanCommandHandler(store),
                    new SetGhgSpanCommand(Body, trend.Id, GhgScale.FormatMonth(from), GhgScale.FormatMonth(to)),
                    (document, read) => HandWrittenGhgEdits.SetSpan(document, GhgEdits.TrendOf(read, trend.Id)!, from, to));
            }
        }

        string[] ids =
        [
            .. Sampled(model.Trends, trend => model.Influences.Any(influence => influence.From == trend.Id || influence.To == trend.Id)).Select(trend => trend.Id),
            .. Sampled(model.Triggers, trigger => model.Influences.Any(influence => influence.From == trigger.Id)).Select(trigger => trigger.Id),
            .. Sampled(model.Notes).Select(note => note.Id),
        ];
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            await Compare($"remove {id}", store => new RemoveGhgElementCommandHandler(store), new RemoveGhgElementCommand(Body, id), (document, read) => HandWrittenGhgEdits.Remove(document, read, id));

            compared.Add($"confirm removing {id}");
            var expected = HandWrittenGhgEdits.RemoveConfirmation(model, id);
            var element = GhgDefinition.ElementOf(GhgBody.Parse(text).Disl.Diagram, id);
            var actual = element is null
                ? null
                : DeletionPolicy.Confirmation(GhgDefinition.Specification, element) is { } confirmation
                    ? $"{confirmation.Title} | {confirmation.Message} | {confirmation.ConfirmLabel} | {confirmation.Danger}"
                    : null;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                mismatches.Add($"confirm removing {id}\n  hand-written {expected ?? "none"}\n  definition   {actual ?? "none"}");
        }

        Assert.True(mismatches.Count == 0, $"{path}: {mismatches.Count} of {compared.Count} differ\n{string.Join("\n", mismatches)}");
        return;

        async Task Compare<TCommand>(string edit, Func<Store, ICommandHandler<TCommand>> handler, TCommand command, Func<GhgBody, GhgModel, GhgEdit> handWritten)
            where TCommand : ICommand
        {
            compared.Add(edit);
            var store = new Store(text);
            var result = await handler(store).ExecuteAsync(command, TestContext.Current.CancellationToken);
            var actual = result.IsSuccess ? store.Saved ?? "(nothing saved)" : $"refused: {result.Error}";

            var document = GhgBody.Parse(text);
            var outcome = handWritten(document, GhgParser.Parse(document));
            var expected = outcome.WasApplied ? document.Text : $"refused: {outcome.Refusal}";
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"{edit}\n{Difference(expected, actual)}");
            }
        }
    }

    /// <summary>Each toolbox drop is the definition's add-here operation at the same point: the same type and the same values.</summary>
    [Theory]
    [InlineData("trend", "addTrendHere")]
    [InlineData("trigger", "addTriggerHere")]
    [InlineData("note", "addNoteHere")]
    public void EveryDrop_IsItsAddHereOperation(string tool, string operation)
    {
        foreach ((var _, string text) in Edited())
        {
            var model = GhgParser.Parse(text);
            foreach ((double x, double y) in Points)
            {
                var position = GhgDefinition.Position(x, y, model.TimeUnit);
                var dropped = OperationInterpreter.Drop(GhgDefinition.Specification, tool, GhgBody.Parse(text).Disl.Diagram, position, DislIds.Fixed(NewId));
                var added = OperationInterpreter.Run(GhgDefinition.Specification, operation, GhgBody.Parse(text).Disl.Diagram, null, DislIds.Fixed(NewId), new DislInvocation(position));

                Assert.Equal(Changes(added), Changes(dropped));
            }
        }
    }

    private static string Changes(DislTransaction transaction) =>
        transaction.Refusal ?? string.Join("\n", transaction.Changes.Select(change => change switch
        {
            DislChange.Create create => $"create {create.Type} {create.Id} {string.Join(", ", create.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value} ({pair.Value?.GetType().Name})"))}",
            _ => change.ToString(),
        }));

    private static string Difference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "(end)";
            var right = index < actualLines.Length ? actualLines[index] : "(end)";
            if (left != right) return $"  first differs at line {index + 1}\n  hand-written {left.TrimEnd('\r')}\n  definition   {right.TrimEnd('\r')}";
        }
        return "  the same lines, different bytes";
    }

    private static IEnumerable<T> Sampled<T>(IReadOnlyList<T> entries, Func<T, bool>? always = null) =>
        entries.Where((entry, index) => index < Sample || (always?.Invoke(entry) ?? false)).Take(Sample * 3);

    private static IEnumerable<(string Path, string Text)> Edited() =>
        GhgDisl.Texts()
            .Where(document => document.Text.Count(character => character == '\n') <= MaxLines && GhgDocumentEntry.Read(document.Text).IsUsable)
            .DistinctBy(document => document.Text, StringComparer.Ordinal);

    /// <summary>A store holding one document that never changes; a save records the text it was given.</summary>
    private sealed class Store(string text) : IGhgDocumentStore
    {
        /// <summary>The text of the last save, or null.</summary>
        public string? Saved { get; private set; }

        public event EventHandler<GhgDocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public GhgDocumentEntry GetOrLoad(string path) => GhgDocumentEntry.Read(text);

        public DocumentSaveResult Save(string path, GhgBody document)
        {
            Saved = document.Text;
            return DocumentSaveResult.Ok;
        }

        public void Forget(string path)
        {
        }

        public void BodyDeleted(string path)
        {
        }

        public void Reload(string path)
        {
        }
    }
}
