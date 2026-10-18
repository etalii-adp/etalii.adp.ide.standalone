using System.Text;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Byte-exact round trips over the parity corpus (causal-loop-disl-fbl Requirements 5.7 and 5.8): a
/// document saved without a change, or with every value set to what it already holds, is written back
/// byte for byte with no splice; and every edit, undone through the shared restore command, puts back the
/// bytes it started from.
/// </summary>
public sealed class CausalLoopRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));

    public CausalLoopRoundTripTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestFolder.TryDelete(_root);

    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    private static Task<string> ReadAsync(string relative) =>
        File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentSavedWithoutAChange_IsWrittenBackByteForByte(string relative)
    {
        // Arrange.
        var text = await ReadAsync(relative);
        var path = Path.Combine(_root, "unchanged.cld");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(text), TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore(new FileWriter());

        // Act.
        var saved = await store.SaveAsync(path, store.GetOrLoad(path).Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(saved.Failed, saved.Error);
        Assert.Equal(text, CausalLoopBody.Parse(text).Text);
        Assert.Equal(Encoding.UTF8.GetBytes(text), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryValueSetToWhatItHolds_SplicesNothing(string relative)
    {
        // Arrange.
        var text = await ReadAsync(relative);
        var elements = CausalLoopBody.Parse(text).Disl.Diagram.Elements.Where(element => Written.ContainsKey(element.Type.Name)).ToList();
        Assert.NotEmpty(elements);

        foreach (var element in elements)
        {
            var body = CausalLoopBody.Parse(text);
            var values = Written[element.Type.Name]
                .Where(element.Attributes.ContainsKey)
                .ToDictionary(name => name, name => element.Attributes[name], StringComparer.Ordinal);

            // Act.
            var outcome = CausalLoopDefinition.Apply(body, new DislTransaction([new DislChange.Set(element.Id, element.Type.Name, values)], [], null));

            // Assert.
            Assert.True(outcome.WasApplied, $"{relative} {element.Id}: {outcome.Refusal}");
            Assert.True(text == body.Text, $"{relative} {element.Id}: setting what it holds changed the text to\n{body.Text}");
        }
    }

    /// <summary>The attributes a statement of each type writes, which an edit can set.</summary>
    private static readonly Dictionary<string, string[]> Written = new(StringComparer.Ordinal)
    {
        ["Variable"] = ["name", "label"],
        ["CausalLink"] = ["polarity", "delayed", "flipped", "weight", "note"],
        ["Loop"] = ["identifier", "title", "members"],
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEditUndone_RestoresTheBytes(string relative)
    {
        // Arrange.
        var text = await ReadAsync(relative);
        var bytes = Encoding.UTF8.GetBytes(text);
        var model = CausalLoopBody.Parse(text).Read();
        var applied = 0;

        foreach ((string name, Func<ICausalLoopDocumentStore, string, Task<CommandResult>> run) in Edits(model))
        {
            var path = Path.Combine(_root, $"{Guid.NewGuid():N}.cld");
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var store = new CausalLoopDocumentStore(new FileWriter());

            // Act.
            var result = await run(store, path);
            if (result.IsSuccess)
            {
                applied++;
                var restore = Assert.IsType<RestoreDocumentCommand<ICausalLoopDocumentStore>>(result.Inverse);
                var undone = await new RestoreDocumentCommandHandler<ICausalLoopDocumentStore>(store, new FileWriter()).ExecuteAsync(restore, TestContext.Current.CancellationToken);
                Assert.True(undone.IsSuccess, $"{relative} {name}: {undone.Error}");
            }

            // Assert: undone, or refused with nothing written.
            var after = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(after), $"{relative} {name}: the bytes did not come back");
        }

        Assert.True(applied >= 10, $"{relative}: only {applied} edits applied, so the round trips prove little.");
    }

    /// <summary>One edit of every command, against the first elements of <paramref name="model"/>.</summary>
    private static IEnumerable<(string Name, Func<ICausalLoopDocumentStore, string, Task<CommandResult>> Run)> Edits(CausalLoopModel model)
    {
        var token = TestContext.Current.CancellationToken;
        var variables = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();
        var first = variables.FirstOrDefault() ?? "nobody";
        var second = variables.Skip(1).FirstOrDefault() ?? first;
        var link = model.Links.FirstOrDefault();
        (string from, string to) = link is null ? ("nobody", "nobody") : (link.From, link.To);
        var loop = model.Loops.FirstOrDefault()?.Identifier ?? "R0";

        yield return ("add variable", (store, path) => new AddVariableCommandHandler(store).ExecuteAsync(new AddVariableCommand(path, "roundTrip", "Round trip"), token));
        yield return ("variable label", (store, path) => new SetVariableLabelCommandHandler(store).ExecuteAsync(new SetVariableLabelCommand(path, first, "Round trip"), token));
        yield return ("variable label cleared", (store, path) => new SetVariableLabelCommandHandler(store).ExecuteAsync(new SetVariableLabelCommand(path, first, ""), token));
        yield return ("rename", (store, path) => new RenameVariableCommandHandler(store).ExecuteAsync(new RenameVariableCommand(path, first, "roundTrip"), token));
        yield return ("remove variable", (store, path) => new RemoveVariableCommandHandler(store).ExecuteAsync(new RemoveVariableCommand(path, first), token));
        yield return ("add link", (store, path) => new AddLinkCommandHandler(store).ExecuteAsync(new AddLinkCommand(path, second, first, CausalLoopPolarity.Negative), token));
        yield return ("polarity", (store, path) => new SetLinkPolarityCommandHandler(store).ExecuteAsync(new SetLinkPolarityCommand(path, from, to, link?.Polarity == CausalLoopPolarity.Negative ? CausalLoopPolarity.Positive : CausalLoopPolarity.Negative), token));
        yield return ("polarity unstated", (store, path) => new SetLinkPolarityCommandHandler(store).ExecuteAsync(new SetLinkPolarityCommand(path, from, to, CausalLoopPolarity.Unstated), token));
        yield return ("delay", (store, path) => new SetLinkDelayCommandHandler(store).ExecuteAsync(new SetLinkDelayCommand(path, from, to, link?.Delayed != true), token));
        yield return ("curvature", (store, path) => new SetLinkCurvatureCommandHandler(store).ExecuteAsync(new SetLinkCurvatureCommand(path, from, to, link?.Flipped != true), token));
        yield return ("weight", (store, path) => new SetLinkWeightCommandHandler(store).ExecuteAsync(new SetLinkWeightCommand(path, from, to, 2.5), token));
        yield return ("weight cleared", (store, path) => new SetLinkWeightCommandHandler(store).ExecuteAsync(new SetLinkWeightCommand(path, from, to, null), token));
        yield return ("note", (store, path) => new SetLinkLabelCommandHandler(store).ExecuteAsync(new SetLinkLabelCommand(path, from, to, "Round trip"), token));
        yield return ("note cleared", (store, path) => new SetLinkLabelCommandHandler(store).ExecuteAsync(new SetLinkLabelCommand(path, from, to, ""), token));
        yield return ("remove link", (store, path) => new RemoveLinkCommandHandler(store).ExecuteAsync(new RemoveLinkCommand(path, from, to), token));
        yield return ("add loop", (store, path) => new AddLoopCommandHandler(store).ExecuteAsync(new AddLoopCommand(path, "R77", "Round trip", [first]), token));
        yield return ("loop name", (store, path) => new SetLoopNameCommandHandler(store).ExecuteAsync(new SetLoopNameCommand(path, loop, "Round trip"), token));
        yield return ("loop identifier", (store, path) => new SetLoopIdentifierCommandHandler(store).ExecuteAsync(new SetLoopIdentifierCommand(path, loop, "R78"), token));
        yield return ("loop membership", (store, path) => new SetLoopMembershipCommandHandler(store).ExecuteAsync(new SetLoopMembershipCommand(path, loop, [second, first]), token));
        yield return ("remove loop", (store, path) => new RemoveLoopCommandHandler(store).ExecuteAsync(new RemoveLoopCommand(path, loop), token));
    }
}
