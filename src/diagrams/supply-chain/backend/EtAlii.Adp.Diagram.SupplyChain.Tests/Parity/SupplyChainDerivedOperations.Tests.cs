using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The edits decided by the DISL definition write what the hand-written handlers wrote (supply-chain-disl-fbl, tasks 7
/// and 11): for every document of the parity corpus, every command at the entries it is sampled at (see
/// <see cref="IdsOf"/>), the empty id and an id nothing holds, gives a body that reads as the same groups, nodes, flows,
/// problems and version, or the same refusal, through today's handlers and through
/// <see cref="HandWrittenSupplyChainEdits"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is read is compared, not the bytes.</b> Since task 11 the handlers write through the binding and the oracle
/// through the line writer, so a value keeps its own style and a key goes where the binding orders it (Q1): the bytes
/// differ by design, and the transcript's regenerated edit hunks are where they are reviewed.
/// </para>
/// <para>
/// <b>Three differences are expected, and nothing else is</b> (<see cref="EditComparison.Expected"/>): a position or a
/// size the oracle rounded to even from a half, which the binding rounds away from zero (Q1); an edit after which the
/// oracle's body no longer reads, which the binding writes readably or refuses (finding B13 for an entry written as a flow
/// mapping, B6 for a list written <c>[ ]</c> or with a comment after it); and every edit of
/// <see cref="DamagedByTheOracle"/>, where the oracle writes inside a value that spans lines (B13, Q6), which
/// <see cref="SupplyChainWriterDefectsTests"/> guards.
/// </para>
/// </remarks>
public class SupplyChainDerivedOperationsTests
{
    private const string Body = "parity.supply";
    private const string NewId = "parity-new";
    private const string Unknown = "parity-unknown";

    /// <summary>How many entries of each list a document is edited at; every handler call reads the whole document again.</summary>
    private const int Sample = 4;

    /// <summary>The documents the oracle damages wherever it edits an entry holding a value that spans lines (finding B13).</summary>
    private static readonly HashSet<string> DamagedByTheOracle = new(StringComparer.Ordinal) { "multi-line-values.supply" };

    /// <summary>Points on the canvas: the origin, a fraction, a half that rounds to even, far out and below zero.</summary>
    private static readonly (double X, double Y)[] _points = [(0, 0), (10.5, 21.5), (333.25, 47.75), (2000, 900), (-40, -30)];

    /// <summary>What a property is set to: a word, a number, a fraction, a negative, nothing, a stage, an old stage's word, a word that is not one, a colon and a hash; with the names and ids of a document's first two groups.</summary>
    private static readonly string[] _values = ["Parity", "12.5", "0", "-1", "", "  ", SupplyChainNodeTypes.Hub, "supplier", "quarry", "a: b", "# c", "on", "lots"];

    private static readonly string[] _keys =
    [
        SupplyChainKeys.Name, SupplyChainKeys.Description, SupplyChainKeys.Type, SupplyChainKeys.Group, SupplyChainKeys.Quantity,
        SupplyChainKeys.Unit, SupplyChainKeys.Step, SupplyChainKeys.Product, SupplyChainKeys.Volume, "parity",
    ];

    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEdit_WritesWhatTheHandWrittenEditWrote(string name)
    {
        // Arrange.
        var text = await SupplyChainCorpus.TextAsync(name, TestContext.Current.CancellationToken);
        var model = SupplyChainReading.Parse(text);
        var run = new EditComparison(text, DamagedByTheOracle.Contains(Path.GetFileName(name)));

        // Act.
        await Adds(run);
        await Connects(run, model);
        foreach (var id in IdsOf(model))
        {
            await EntryEdits(run, model, id);
        }

        await run.Compare("place every node", store => new PlaceSupplyChainNodesCommandHandler(store), new PlaceSupplyChainNodesCommand(Body, Places(model)), store => new HandWrittenSupplyChainEdits.PlaceSupplyChainNodes(store));
        await run.Compare("arrange", store => new ArrangeSupplyChainCommandHandler(store), new ArrangeSupplyChainCommand(Body), store => new HandWrittenSupplyChainEdits.ArrangeSupplyChain(store));

        // Assert.
        Assert.True(run.Mismatches.Count == 0, $"{name}: {run.Mismatches.Count} of {run.Compared} differ\n{string.Join("\n", run.Mismatches)}");
    }

    private static async Task Adds(EditComparison run)
    {
        foreach ((double x, double y) in _points)
        {
            foreach (var stage in SupplyChainNodeTypes.All.Append("supplier").Append("quarry"))
            {
                await run.Compare($"add {stage} at {x}, {y}", store => new AddSupplyChainNodeCommandHandler(store), new AddSupplyChainNodeCommand(Body, stage, x, y, NewId), store => new HandWrittenSupplyChainEdits.AddSupplyChainNode(store));
            }

            await run.Compare($"add a group at {x}, {y}", store => new AddSupplyChainGroupCommandHandler(store), new AddSupplyChainGroupCommand(Body, x, y, NewId), store => new HandWrittenSupplyChainEdits.AddSupplyChainGroup(store));
        }
    }

    private static async Task Connects(EditComparison run, SupplyChainModel model)
    {
        var ends = IdsOf(model);
        foreach (var from in ends)
        {
            foreach (var to in ends)
            {
                await run.Compare($"connect {from} to {to}", store => new ConnectSupplyChainNodesCommandHandler(store), new ConnectSupplyChainNodesCommand(Body, from, to, NewId), store => new HandWrittenSupplyChainEdits.ConnectSupplyChainNodes(store));
            }
        }

        if (model.Nodes.FirstOrDefault(node => node.Id.Length > 0) is { } first && model.Nodes.LastOrDefault(node => node.Id.Length > 0) is { } last)
        {
            await run.Compare($"connect {first.Id} to {last.Id} as {first.Id}", store => new ConnectSupplyChainNodesCommandHandler(store), new ConnectSupplyChainNodesCommand(Body, first.Id, last.Id, first.Id), store => new HandWrittenSupplyChainEdits.ConnectSupplyChainNodes(store));
        }
    }

    private static async Task EntryEdits(EditComparison run, SupplyChainModel model, string id)
    {
        await run.Compare($"remove {id}", store => new RemoveSupplyChainEntryCommandHandler(store), new RemoveSupplyChainEntryCommand(Body, id), store => new HandWrittenSupplyChainEdits.RemoveSupplyChainEntry(store));
        foreach (var steps in (int[])[1, -1, 3, -12, 0])
        {
            await run.Compare($"step {id} by {steps}", store => new StepSupplyChainValueCommandHandler(store), new StepSupplyChainValueCommand(Body, id, steps), store => new HandWrittenSupplyChainEdits.StepSupplyChainValue(store));
        }

        foreach (var groupName in (string[])["Parity", " ", "Parity: #1"])
        {
            await run.Compare($"put {id} in a new group {groupName}", store => new GroupSupplyChainNodeCommandHandler(store), new GroupSupplyChainNodeCommand(Body, id, groupName, NewId), store => new HandWrittenSupplyChainEdits.GroupSupplyChainNode(store));
        }

        var taken = IdsOf(model).FirstOrDefault(candidate => candidate.Length > 0 && candidate != Unknown && candidate != id);
        if (taken is not null)
        {
            await run.Compare($"put {id} in a new group as {taken}", store => new GroupSupplyChainNodeCommandHandler(store), new GroupSupplyChainNodeCommand(Body, id, "Parity", taken), store => new HandWrittenSupplyChainEdits.GroupSupplyChainNode(store));
        }

        foreach (var key in _keys)
        {
            foreach (var value in _values.Concat(model.Groups.Take(2).SelectMany(group => (string[])[group.Name, group.Id])).Distinct(StringComparer.Ordinal))
            {
                await run.Compare($"set {id} {key} = \"{value}\"", store => new SetSupplyChainPropertyCommandHandler(store), new SetSupplyChainPropertyCommand(Body, id, key, value), store => new HandWrittenSupplyChainEdits.SetSupplyChainProperty(store));
            }
        }

        foreach ((double x, double y) in _points.Concat(Frames(model)))
        {
            await run.Compare($"move {id} to {x}, {y}", store => new MoveSupplyChainEntryCommandHandler(store), new MoveSupplyChainEntryCommand(Body, id, x, y), store => new HandWrittenSupplyChainEdits.MoveSupplyChainEntry(store));
        }

        await run.Compare($"place {id}", store => new PlaceSupplyChainNodesCommandHandler(store), new PlaceSupplyChainNodesCommand(Body, new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { [id] = (10.5, 21.5) }), store => new HandWrittenSupplyChainEdits.PlaceSupplyChainNodes(store));
    }

    /// <summary>
    /// The ids edited at: those of the first <see cref="Sample"/> groups, nodes and flows, every id more than one entry
    /// is written with, each once, and the empty id and one nothing holds.
    /// </summary>
    private static List<string> IdsOf(SupplyChainModel model)
    {
        var written = model.Groups.Select(group => group.Id).Concat(model.Nodes.Select(node => node.Id)).Concat(model.Flows.Select(flow => flow.Id)).ToList();
        return
        [
            .. model.Groups.Take(Sample).Select(group => group.Id)
                .Concat(model.Nodes.Take(Sample).Select(node => node.Id))
                .Concat(model.Flows.Take(Sample).Select(flow => flow.Id))
                .Concat(written.GroupBy(id => id, StringComparer.Ordinal).Where(ids => ids.Count() > 1).Select(ids => ids.Key))
                .Append("")
                .Append(Unknown)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>A point inside every group's frame, where a node dropped joins it.</summary>
    private static IEnumerable<(double X, double Y)> Frames(SupplyChainModel model)
    {
        var layout = SupplyChainLayout.Of(model);
        return layout.GroupBoxes.Values.Select(box => (box.X + (box.Width / 2) - (SupplyChainGeometry.NodeWidth / 2), box.Y + (box.Height / 2) - (SupplyChainGeometry.NodeHeight / 2)));
    }

    private static Dictionary<string, (double X, double Y)> Places(SupplyChainModel model) =>
        model.Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Select((id, index) => (id, index))
            .ToDictionary(entry => entry.id, entry => (100.0 + (entry.index * 37.5), 50.5), StringComparer.Ordinal);

    /// <summary>The comparisons of one document: how many were made and what differed.</summary>
    private sealed class EditComparison(string text, bool damagedByTheOracle)
    {
        public int Compared { get; private set; }

        public List<string> Mismatches { get; } = [];

        public async Task Compare<TCommand>(string edit, Func<SupplyChainParityStore, ICommandHandler<TCommand>> derived, TCommand command, Func<HandWrittenParityStore, ICommandHandler<TCommand>> handWritten)
            where TCommand : ICommand
        {
            Compared++;
            var handWrittenStore = new HandWrittenParityStore(text);
            var expected = await OutcomeOf(handWritten(handWrittenStore), () => handWrittenStore.Saved, command);
            var derivedStore = new SupplyChainParityStore(text);
            var actual = await OutcomeOf(derived(derivedStore), () => derivedStore.Saved, command);
            if (!Expected(expected, actual))
            {
                Mismatches.Add($"{edit}\n{Difference(expected.ToString(), actual.ToString())}");
            }
        }

        /// <summary>
        /// Whether <paramref name="actual"/> is what the definition is expected to write where the oracle wrote
        /// <paramref name="expected"/>: the same, but for the three differences the class remarks name.
        /// </summary>
        private bool Expected(Outcome expected, Outcome actual)
        {
            if (expected.Reading is null || actual.Reading is null)
            {
                return expected.Text == actual.Text || (Damaged(expected) && actual.Reading is not { IsUsable: false });
            }

            if (Damaged(expected) || damagedByTheOracle)
            {
                return actual.Reading.IsUsable;
            }

            (SupplyChainModel before, SupplyChainModel after) = (expected.Reading.Model, actual.Reading.Model);
            return expected.Reading.Unreadable == actual.Reading.Unreadable
                && before.Version == after.Version
                && before.Problems.Select(problem => problem.Message).SequenceEqual(after.Problems.Select(problem => problem.Message))
                && before.Groups.Count == after.Groups.Count
                && before.Groups.Zip(after.Groups).All(pair => Halves(pair.First.X, pair.Second.X) && Halves(pair.First.Y, pair.Second.Y) && pair.First with { Range = default, X = pair.Second.X, Y = pair.Second.Y } == pair.Second with { Range = default })
                && before.Nodes.Count == after.Nodes.Count
                && before.Nodes.Zip(after.Nodes).All(pair => Halves(pair.First.X, pair.Second.X) && Halves(pair.First.Y, pair.Second.Y) && pair.First with { Range = default, X = pair.Second.X, Y = pair.Second.Y } == pair.Second with { Range = default })
                && before.Flows.Count == after.Flows.Count
                && before.Flows.Zip(after.Flows).All(pair => pair.First with { Range = default } == pair.Second with { Range = default });
        }

        /// <summary>Whether the oracle's edit left a body that no longer reads, which the body it edited did.</summary>
        private bool Damaged(Outcome outcome) => outcome.Reading is { IsUsable: false } && SupplyChainReading.Outcome(text).IsUsable;

        /// <summary>
        /// Whether a whole number the definition wrote is the one the oracle wrote, or the half the oracle rounded to even
        /// rounded away from zero instead: one further from zero, in the same direction. <c>-0</c> and <c>0</c> are the same.
        /// </summary>
        private static bool Halves(double? handWritten, double? definition)
        {
            if (handWritten is not { } before || definition is not { } after)
            {
                return handWritten is null && definition is null;
            }

            return Math.Abs(after - before) < double.Tolerance
                || (Math.Abs(Math.Abs(after) - Math.Abs(before) - 1) < double.Tolerance
                    && (Math.Abs(before) < double.Tolerance || Math.Sign(after) == Math.Sign(before)));
        }

        private static async Task<Outcome> OutcomeOf<TCommand>(ICommandHandler<TCommand> handler, Func<string?> saved, TCommand command)
            where TCommand : ICommand
        {
            try
            {
                var result = await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);
                if (!result.IsSuccess)
                {
                    return new Outcome($"refused: {result.Error}", null);
                }

                return saved() is { } written
                    ? new Outcome(written, SupplyChainReading.Outcome(written))
                    : new Outcome("(nothing saved)", null);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
            {
                return new Outcome($"throws {exception.GetType().Name}: {exception.Message}", null);
            }
        }

        /// <summary>The lines of two outcomes that differ.</summary>
        private static string Difference(string expected, string actual)
        {
            var left = expected.Split('\n');
            var right = actual.Split('\n');
            return string.Join("\n", left.Except(right).Select(line => $"  hand-written {line}").Concat(right.Except(left).Select(line => $"  definition   {line}")));
        }
    }

    /// <summary>What an edit gave: its refusal or the text it saved, and what that text reads as.</summary>
    private sealed record Outcome(string Text, ParseOutcome<SupplyChainModel>? Reading)
    {
        /// <summary>What the edit gave, without the lines it is read from: every entry, problem and the version, or the refusal.</summary>
        public override string ToString()
        {
            if (Reading is null)
            {
                return Text;
            }

            var model = Reading.Model;
            return string.Join(
                "\n",
                [
                    $"unreadable {Reading.Unreadable}",
                    $"version {model.Version}",
                    .. model.Groups.Select(group => (group with { Range = default }).ToString()),
                    .. model.Nodes.Select(node => (node with { Range = default }).ToString()),
                    .. model.Flows.Select(flow => (flow with { Range = default }).ToString()),
                    .. model.Problems.Select(problem => problem.Message),
                ]).ReplaceLineEndings("\n");
        }
    }
}
