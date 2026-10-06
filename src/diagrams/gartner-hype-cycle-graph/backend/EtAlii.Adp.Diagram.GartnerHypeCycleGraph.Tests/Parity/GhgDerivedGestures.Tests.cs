using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The gesture refusals derived from the DISL definition's <c>change</c> and <c>placement</c>
/// constraints equal what the command handlers refuse (runtime plan step S12): for every trend,
/// trigger and note of the parity corpus, over a matrix of values around the element's own, the
/// handler refuses exactly when the definition does, and with the definition's first refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only values the gesture can carry are compared.</b> Text that is not a date or a size is
/// refused by the host before it is a value (the property grid's typing), so it is not a constraint's
/// to refuse; an element the handler cannot find is not either.
/// </para>
/// <para>
/// <b>An id held by more than one entry is skipped</b>: the handler edits the first holder of the
/// list it looks in first, which is a question of the host's lookup and not of a constraint.
/// </para>
/// <para>
/// <b>A long document is sampled</b>: every handler call reads the whole document again, so of a
/// document with more than <see cref="Sample"/> entries of a list only the first that many are
/// gestured on, with every trend whose dates cannot be read and every note whose month cannot be read
/// besides. A document of more than <see cref="MaxLines"/> lines is left out, at most a second's read
/// per gesture, and a document shipped twice is compared once. The fixtures are short and are compared
/// whole.
/// </para>
/// </remarks>
public class GhgDerivedGesturesTests
{
    private const string Body = "parity.ghg";

    /// <summary>How many entries of each list a long document is gestured on.</summary>
    private const int Sample = 3;

    /// <summary>The longest document gestured on.</summary>
    private const int MaxLines = 1000;

    public static TheoryData<string> Documents() => [.. Gestured().Select(document => document.Path)];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryChangeRefusal_IsTheHandlers(string path)
    {
        var text = GhgDisl.Texts().Single(document => document.Path == path).Text;
        var store = new Store(text);
        var model = GhgParser.Parse(text);
        var diagram = GhgBody.Parse(text).Disl.Diagram;
        var unique = UniqueIds(model);
        List<string> mismatches = [];
        var compared = 0;

        foreach (var trend in Sampled(model.Trends, trend => !trend.HasSpan).Where(trend => unique.Contains(trend.Id)))
        {
            var self = diagram.ElementById(trend.Id)!;
            var start = trend.Start ?? 1900 * 12;
            var stop = trend.Stop ?? start + 12;

            foreach (var month in Around(start, stop))
            {
                var handled = await RunAsync(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trend.Id, GhgScale.FormatMonth(month), null));
                Compare($"{trend.Id} start {GhgScale.FormatMonth(month)}", handled, GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "start", (long)month));
                handled = await RunAsync(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trend.Id, null, GhgScale.FormatMonth(month)));
                Compare($"{trend.Id} stop {GhgScale.FormatMonth(month)}", handled, GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "stop", (long)month));
            }

            for (var phases = -1; phases <= 6; phases++)
            {
                var handled = await RunAsync(new SetGhgPhasesCommandHandler(store), new SetGhgPhasesCommand(Body, trend.Id, phases));
                Compare($"{trend.Id} phases {phases}", handled, GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "phases", (long)phases));
            }

            await RenameAsync(trend.Id, self, "name");

            if (trend.HasSpan)
            {
                foreach (var month in Around(start, stop))
                {
                    foreach ((int from, int to) in new[] { (month, stop), (start, month), (month, month + (stop - start)) })
                    {
                        var handled = await RunAsync(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trend.Id, GhgScale.FormatMonth(from), GhgScale.FormatMonth(to)));
                        var derived = GestureConstraintEvaluator.Placement(
                            GhgDefinition.Specification, self, "resize", Bounds(start, stop), Bounds(from, to));
                        Compare($"{trend.Id} resize {GhgScale.FormatMonth(from)}..{GhgScale.FormatMonth(to)}", handled, derived);
                    }
                }
            }
        }

        foreach (var trigger in Sampled(model.Triggers).Where(trigger => unique.Contains(trigger.Id)))
        {
            var self = diagram.ElementById(trigger.Id)!;
            foreach (var month in Around(trigger.Date ?? 1900 * 12, (trigger.Date ?? 1900 * 12) + 1))
            {
                var handled = await RunAsync(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trigger.Id, GhgScale.FormatMonth(month), null));
                Compare($"{trigger.Id} date {GhgScale.FormatMonth(month)}", handled, GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "date", (long)month));
            }

            await RenameAsync(trigger.Id, self, "name");
        }

        foreach (var note in Sampled(model.Notes, note => note.At is null).Where(note => unique.Contains(note.Id)))
        {
            var self = diagram.ElementById(note.Id)!;
            foreach ((double width, double height) in new[] { (160.0, 64.0), (0.0, 64.0), (160.0, 0.0), (0.0, 0.0), (12.5, 1.0) })
            {
                var handled = await RunAsync(new SetGhgNoteSizeCommandHandler(store), new SetGhgNoteSizeCommand(Body, note.Id, SetGhgNoteSizeCommand.Format(width, height)));
                var derived = GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "width", width) is { Count: > 0 } refused
                    ? refused
                    : GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, "height", height);
                Compare($"{note.Id} size {width} x {height}", handled, derived);
            }

            await RenameAsync(note.Id, self, "text");
        }

        Assert.True(mismatches.Count == 0, $"{path}: {mismatches.Count} of {compared} differ\n{string.Join("\n", mismatches)}");
        return;

        async Task RenameAsync(string id, DislElement self, string attribute)
        {
            foreach (var name in new[] { "", "   ", "Renamed", "  Padded  " })
            {
                var handled = await RunAsync(new RenameGhgElementCommandHandler(store), new RenameGhgElementCommand(Body, id, name));
                Compare($"{id} {attribute} '{name}'", handled, GestureConstraintEvaluator.Change(GhgDefinition.Specification, self, attribute, name));
            }
        }

        void Compare(string gesture, CommandResult handled, IReadOnlyList<DislFinding> derived)
        {
            compared++;
            var expected = handled.IsSuccess ? "allowed" : $"refused: {handled.Error}";
            var actual = derived.Count == 0 ? "allowed" : $"refused: {derived[0].Message}";
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"{gesture}\n  handler    {expected}\n  definition {actual}");
            }
        }
    }

    /// <summary>The corpus reaches a refusal of every gesture rule, so the comparison above is not vacuous.</summary>
    [Fact]
    public async Task TheCorpus_ReachesEveryRefusal()
    {
        HashSet<string> refused = new(StringComparer.Ordinal);
        foreach ((var _, string text) in Gestured())
        {
            var store = new Store(text);
            var model = GhgParser.Parse(text);
            foreach (var trend in Sampled(model.Trends, trend => !trend.HasSpan).Where(trend => trend.Id.Length > 0))
            {
                var start = trend.Start ?? 1900 * 12;
                var stop = trend.Stop ?? start + 12;
                foreach (var month in Around(start, stop))
                {
                    await Collect(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trend.Id, GhgScale.FormatMonth(month), null));
                    await Collect(new SetGhgSpanCommandHandler(store), new SetGhgSpanCommand(Body, trend.Id, GhgScale.FormatMonth(month), GhgScale.FormatMonth(month)));
                }

                for (var phases = -1; phases <= 6; phases++) await Collect(new SetGhgPhasesCommandHandler(store), new SetGhgPhasesCommand(Body, trend.Id, phases));
                await Collect(new RenameGhgElementCommandHandler(store), new RenameGhgElementCommand(Body, trend.Id, " "));
            }

            foreach (var trigger in Sampled(model.Triggers).Where(trigger => trigger.Id.Length > 0))
            {
                await Collect(new RenameGhgElementCommandHandler(store), new RenameGhgElementCommand(Body, trigger.Id, " "));
            }

            foreach (var note in Sampled(model.Notes, note => note.At is null).Where(note => note.Id.Length > 0))
            {
                await Collect(new SetGhgNoteSizeCommandHandler(store), new SetGhgNoteSizeCommand(Body, note.Id, "0 x 64"));
            }
        }

        string[] expected =
        [
            "A note needs a width and a height.",
            "A trend must stop after it starts, at least one month later.",
            "A trend needs a name.",
            "A trend shows 1 to 4 phases.",
            "A trigger needs a name.",
            "This note's position cannot be read, so it cannot be resized until it is fixed in the file.",
            "This trend's dates cannot be read, so it cannot be changed until they are fixed in the file.",
        ];
        Assert.All(expected, message => Assert.Contains(message, refused));
        Assert.Contains(refused, message => message.StartsWith("A trend showing ", StringComparison.Ordinal));

        // "A trend must be at least one month long." is not reached, and cannot be: it is the refusal
        // for a span shorter than one month, which only a stop at or before the start makes, and that
        // is refused first as a stop before the start. The definition's tooShort keeps the sentence,
        // as GhgEdits.TooShort does.
        return;

        async Task Collect<TCommand>(ICommandHandler<TCommand> handler, TCommand command)
            where TCommand : ICommand
        {
            var result = await RunAsync(handler, command);
            if (!result.IsSuccess) refused.Add(result.Error);
        }
    }

    /// <summary>The documents gestured on: readable, at most <see cref="MaxLines"/> lines long, each text once.</summary>
    private static IEnumerable<(string Path, string Text)> Gestured() =>
        GhgDisl.Texts()
            .Where(document => document.Text.Count(character => character == '\n') <= MaxLines && GhgDocumentEntry.Read(document.Text).IsUsable)
            .DistinctBy(document => document.Text, StringComparer.Ordinal);

    private static async Task<CommandResult> RunAsync<TCommand>(ICommandHandler<TCommand> handler, TCommand command)
        where TCommand : ICommand =>
        await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);

    /// <summary>Months around a span: either side of each end, on each end, and a phase's worth inside.</summary>
    private static IEnumerable<int> Around(int start, int stop) =>
        new[] { start - 12, start - 1, start, start + 1, start + 2, start + 3, start + 4, stop - 4, stop - 3, stop - 1, stop, stop + 1, stop + 12 }.Distinct();

    /// <summary>The first <see cref="Sample"/> of <paramref name="entries"/>, and every later one <paramref name="always"/> takes.</summary>
    private static IEnumerable<T> Sampled<T>(IReadOnlyList<T> entries, Func<T, bool>? always = null) =>
        entries.Where((entry, index) => index < Sample || (always?.Invoke(entry) ?? false));

    private static Dictionary<string, object?> Bounds(int from, int to) => new(StringComparer.Ordinal) { ["x"] = (long)from, ["x2"] = (long)to };

    /// <summary>The ids exactly one entry holds, of all four lists together.</summary>
    private static HashSet<string> UniqueIds(GhgModel model) =>
    [
        .. model.Trends.Select(trend => trend.Id)
            .Concat(model.Triggers.Select(trigger => trigger.Id))
            .Concat(model.Notes.Select(note => note.Id))
            .Concat(model.Influences.Select(influence => influence.Id))
            .Where(id => id.Length > 0)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key),
    ];

    /// <summary>A store holding one document that never changes: every edit starts from the same text, and a save is a no-op.</summary>
    private sealed class Store(string text) : IGhgDocumentStore
    {
        public event EventHandler<GhgDocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public GhgDocumentEntry GetOrLoad(string path) => GhgDocumentEntry.Read(text);

        public DocumentSaveResult Save(string path, GhgBody document) => DocumentSaveResult.Ok;

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
