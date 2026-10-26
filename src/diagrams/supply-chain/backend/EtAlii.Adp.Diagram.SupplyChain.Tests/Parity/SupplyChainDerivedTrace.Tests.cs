using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>
/// The trace the definition states against the one the module computes (supply-chain-disl-fbl, task 9): for every document
/// of the parity corpus, with nothing and then every drawn node, flow and group selected in turn, the mark
/// <c>traceOf(self)</c> gives every drawn element - read through <c>reachable</c>, <c>upFrom</c>, <c>downFrom</c> and the
/// bound <c>view.selected</c> - against the mark <see cref="SupplyChainTrace.Through"/> gives it.
/// </summary>
/// <remarks>
/// <para>
/// <b>They differ, so <see cref="SupplyChainTrace"/> stays</b>, and the difference is gap 12 of the companion,
/// <c>supply-chain.md</c>: the module walks the drawn flows, while <c>reachable('Flow')</c> walks every flow the model
/// holds, also one without an id, so a node reached only through a flow that is not drawn is lit where the module dims it.
/// This test holds the marks to exactly that difference: a mark that comes to differ anywhere else fails it, and so does one
/// of these that comes to agree, which is when the gap is closed.
/// </para>
/// <para>
/// <b>A selected flow on a cycle differed too, and the definition was repaired</b> (etalii.adp, the companion's
/// <i>Definition defects repaired</i>): the module marks the flow's consumer downstream and its supplier upstream before
/// either walk, where the definition asked first what the walks reached, and so marked the consumer, which on a cycle
/// also supplies the supplier, upstream (<c>mixed-indentation.supply</c>).
/// </para>
/// </remarks>
public class SupplyChainDerivedTraceTests
{
    public static TheoryData<string> Documents() => SupplyChainCorpus.Names();

    /// <summary>Where the definition's marks differ from the module's, by document: what is selected, and the mark of what.</summary>
    private static readonly Dictionary<string, string[]> Recorded = new(StringComparer.Ordinal)
    {
        // A flow without an id, a to c, which is not drawn.
        ["findings-order.supply"] =
        [
            "\"a\" selected: \"c\" is 'dimmed', the definition says 'downstream'",
            "\"c\" selected: \"a\" is 'dimmed', the definition says 'upstream'",
        ],
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryMark_IsTheModulesTrace_ButWhereRecorded(string name)
    {
        // Arrange.
        var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);
        var layout = SupplyChainLayout.Of(model);
        var drawn = new SupplyChainElementMapper().All(model).Select(element => element.Id).ToList();
        var traced = new DefinitionTrace(model);

        // Act and assert.
        var differences = new List<string>();
        foreach (var selected in drawn.Prepend(null))
        {
            var expected = SupplyChainTrace.Through(layout, selected);
            var actual = traced.Through(selected);
            differences.AddRange(drawn
                .Where(id => expected.Of(id) != actual.GetValueOrDefault(id))
                .Select(id => $"{TranscriptText.Quoted(selected ?? "")} selected: {TranscriptText.Quoted(id)} is '{expected.Of(id)}', the definition says '{actual.GetValueOrDefault(id)}'"));
        }

        var recorded = Recorded.GetValueOrDefault(Path.GetFileName(name), []);
        Assert.True(differences.SequenceEqual(recorded), $"{name}:\nrecorded {string.Join("\n         ", recorded)}\nfound    {string.Join("\n         ", differences)}");
    }

    [Fact]
    public void EveryRecordedDifference_IsInTheCorpus() =>
        Assert.All(Recorded.Keys, document => Assert.Contains(SupplyChainCorpus.Names(), row => Path.GetFileName(row.Data) == document));

    [Fact]
    public async Task TheCorpus_ReachesEveryMark()
    {
        // Arrange.
        var marks = new HashSet<string>(StringComparer.Ordinal);

        // Act.
        foreach (var name in SupplyChainCorpus.Names().Select(row => row.Data))
        {
            var model = await SupplyChainCorpus.ReadAsync(name, TestContext.Current.CancellationToken);
            var traced = new DefinitionTrace(model);
            foreach (var selected in new SupplyChainElementMapper().All(model).Select(element => element.Id))
            {
                marks.UnionWith(traced.Through(selected).Values);
            }
        }

        // Assert.
        Assert.Equal(
            [SupplyChainTrace.Dimmed, SupplyChainTrace.Downstream, SupplyChainTrace.Related, SupplyChainTrace.Selected, SupplyChainTrace.Upstream],
            marks.Order(StringComparer.Ordinal));
    }

    /// <summary>The marks the definition's <c>traceOf</c> gives a document's elements while one of them is selected.</summary>
    private sealed class DefinitionTrace(SupplyChainModel model)
    {
        private static readonly CelProgram TraceOf = SupplyChainDefinition.Specification.Environment(DislContext.Element).Compile("traceOf(self)");

        private readonly DislDiagram _diagram = SupplyChainDisl.Build(model).Diagram;

        /// <summary>Each drawn element's mark, by the id it is written with, while <paramref name="selectedId"/> is selected.</summary>
        public Dictionary<string, string> Through(string? selectedId)
        {
            var selected = SupplyChainDefinition.ElementOf(_diagram, selectedId);
            foreach (var element in _diagram.Nodes.Concat(_diagram.Relations))
            {
                element.View = new CelMap { ["selected"] = ReferenceEquals(element, selected) };
            }

            var marks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var element in _diagram.Nodes.Concat(_diagram.Relations))
            {
                var id = SupplyChainWritten.IdOf(element);
                if (!marks.ContainsKey(id) && ReferenceEquals(SupplyChainDefinition.ElementOf(_diagram, id), element))
                {
                    marks[id] = TraceOf.Evaluate(new Dictionary<string, object?> { ["self"] = element, ["diagram"] = _diagram, ["env"] = _diagram.Env.ToCel() }) switch
                    {
                        string mark => mark,
                        var other => $"<{other}>",
                    };
                }
            }

            return marks;
        }
    }
}
