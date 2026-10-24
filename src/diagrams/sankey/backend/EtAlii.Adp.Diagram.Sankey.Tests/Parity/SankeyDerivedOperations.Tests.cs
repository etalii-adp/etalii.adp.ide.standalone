using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The definition's operations write what the module's commands write (sankey-disl-fbl task 11): for every readable
/// document of the parity corpus, <c>rename</c> on every node and <c>increaseValue</c> and <c>decreaseValue</c> on every
/// flow, run by <see cref="OperationInterpreter"/> and written through the binding, give the same bytes, or the same
/// refusal, as the commands the menus dispatch, except where <see cref="KnownDifferences"/> says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// <b>The commands do not run the operations.</b> A command steps by any number of steps and keeps today's sentences,
/// and a flow's id is derived: the definition names a flow <c>a-&gt;m</c>, the binding knows it by its place. This test
/// finds the element an operation changes by its line, which only a host can do, and so shows that what the definition
/// states is what the tool writes.
/// </para>
/// <para>
/// <b><c>thicker</c> and <c>thinner</c> are left out</b>: their <c>set</c> with <c>target: diagram</c> is refused by the
/// interpreter ("has no element to change"), because the DISL model has no element for the document's own keys.
/// </para>
/// </remarks>
public class SankeyDerivedOperationsTests
{
    private const string Renamed = "Parity renamed";

    /// <summary>
    /// Where the definition writes otherwise than the command, by document and step. <c>number-rounding.skv</c>: 0.0001
    /// stepped by 0.00015 is 0.00025, which the command rounds to even (0.0002) and the definition's <c>math.round</c>
    /// half away from zero (0.0003), finding B15.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownDifferences = new(StringComparer.Ordinal)
    {
        ["number-rounding.skv"] = ["increaseValue a->b"],
    };

    public static TheoryData<string> Corpus() => SankeyCorpus.Names();

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EveryOperation_WritesWhatItsCommandWrites(string name)
    {
        // Arrange.
        var document = new SankeyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var bytes = await document.Read(TestContext.Current.CancellationToken);
        var text = TranscriptText.Decode(bytes);
        var body = SankeyBody.Parse(text);
        if (body.IsReadOnly)
        {
            return;
        }

        var diagram = body.Disl.Diagram;
        List<(string Step, string Operation, DislElement Self, DislInvocation? Invocation, Func<string, ICommand> Command)> steps =
        [
            .. diagram.NodesOfType("Node").Where(node => node.IdIsStored).Select(node => (
                $"rename {node.Id}", "rename", node, (DislInvocation?)new DislInvocation(Parameters: new Dictionary<string, object?> { ["name"] = Renamed }),
                (Func<string, ICommand>)(path => new SetSankeyPropertyCommand(path, node.Id, SankeyKeys.Name, Renamed)))),
            .. diagram.RelationsOfType("Flow").SelectMany(flow => new[] { ("increaseValue", 1), ("decreaseValue", -1) }.Select(step => (
                $"{step.Item1} {flow.Id}", step.Item1, flow, (DislInvocation?)null,
                (Func<string, ICommand>)(path => new StepSankeyValueCommand(path, flow.Id, step.Item2))))),
        ];

        // Act.
        List<string> mismatches = [];
        List<string> differing = [];
        foreach ((string step, string operation, DislElement self, DislInvocation? invocation, Func<string, ICommand> command) in steps)
        {
            if (SankeyDefinition.ElementOf(diagram, self.Id) != self)
            {
                // A later holder of an id is not the entry the command names.
                continue;
            }

            var expected = await CommandAsync(name, bytes, command);
            var actual = Operation(text, operation, self.Id, invocation);
            if (expected != actual)
            {
                differing.Add(step);
                mismatches.Add($"{step}: {Difference(expected, actual)}");
            }
        }

        // Assert: what differs is exactly what is known to.
        Assert.True(
            differing.SequenceEqual(KnownDifferences.GetValueOrDefault(Path.GetFileName(name), []), StringComparer.Ordinal),
            string.Join("\n", mismatches));
    }

    /// <summary>The first line where <paramref name="expected"/> and <paramref name="actual"/> differ, each as written.</summary>
    private static string Difference(string expected, string actual)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        var line = Enumerable.Range(0, Math.Min(left.Length, right.Length)).FirstOrDefault(index => left[index] != right[index], Math.Min(left.Length, right.Length));
        string At(string[] lines) => line < lines.Length ? lines[line].TrimEnd('\r') : "(end)";
        return $"line {line + 1}: command {TranscriptText.Quoted(At(left))}, operation {TranscriptText.Quoted(At(right))}";
    }

    /// <summary>What the command leaves in the file, or its refusal.</summary>
    private static async Task<string> CommandAsync(string name, byte[] bytes, Func<string, ICommand> command)
    {
        using var folder = TestFolder.Create(nameof(SankeyDerivedOperationsTests));
        var path = Path.Combine(folder.Path, Path.GetFileName(name));
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var dispatcher = TestHistory.Dispatcher(services => services.AddSankey());
        var result = await dispatcher.DispatchAsync(command(path), TestContext.Current.CancellationToken);
        return result.IsSuccess
            ? TranscriptText.Decode(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
            : $"refused: {result.Error}";
    }

    /// <summary>What the definition's operation writes through the binding, or its refusal.</summary>
    private static string Operation(string text, string operation, string selfId, DislInvocation? invocation)
    {
        var body = SankeyBody.Parse(text);
        var diagram = body.Disl.Diagram;
        var self = SankeyDefinition.ElementOf(diagram, selfId);
        var transaction = OperationInterpreter.Run(SankeyDefinition.Specification, operation, diagram, self, DislIds.Fixed(), invocation, new DislEnv());
        if (!transaction.WasApplied)
        {
            return $"refused: {transaction.Refusal}";
        }

        foreach (var change in transaction.Changes)
        {
            var written = DislWrite.ToFbl(SankeyDefinition.Specification, change);
            if (written is not ModelChange.Set set || EntryOf(body, diagram, set.Id) is not { } entry)
            {
                return $"not written: {change}";
            }

            var edit = body.Change(set with { Id = entry });
            if (!edit.WasApplied)
            {
                return $"refused: {edit.Refusal}";
            }
        }

        return body.Text;
    }

    /// <summary>The binding's id of the element the definition calls <paramref name="id"/>, found by its line.</summary>
    private static string? EntryOf(SankeyBody body, DislDiagram diagram, string id)
    {
        if (SankeyDefinition.ElementOf(diagram, id) is not { Line: { } line } element)
        {
            return null;
        }

        var rule = element.Type.IsRelation ? SankeyReader.FlowRule : SankeyReader.NodeRule;
        return body.Model.Elements.FirstOrDefault(candidate => candidate.Rule == rule && candidate.Line == line)?.Id;
    }
}
