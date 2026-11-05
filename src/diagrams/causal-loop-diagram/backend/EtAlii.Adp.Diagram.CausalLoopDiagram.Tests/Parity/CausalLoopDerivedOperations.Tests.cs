using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Each menu entry and gesture that now runs the definition's operation writes the bytes the hand-written command
/// wrote, or is refused with its sentence (causal-loop-disl-fbl Requirements 3.5, 5.6 and 7.2): the providers are
/// driven as the host drives them, the command they dispatch is run, and what it writes is held to what
/// <see cref="HandWrittenCausalLoop.Commit"/>'s command writes, for every document of the parity corpus and every
/// element an entry applies to.
/// </summary>
/// <remarks>
/// <para>
/// A document whose variables share a name made the hand-written claim throw before it dispatched (finding A7), so
/// it has no claim to compare; the converted tool answers it, which the transcript records.
/// </para>
/// <para>
/// Making a link positive or negative and renaming a loop dispatch the records they always did; the operations
/// they are derived from are held to those records here as well, except where the menu offers one as unavailable.
/// </para>
/// </remarks>
public class CausalLoopDerivedOperationsTests
{
    private const string Path = "derived.cld";
    private const string Nobody = "nobody";

    public static TheoryData<string> Documents() => [.. CausalLoopCorpus.WalkedElementByElement()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEntry_WritesWhatTheHandWrittenCommandWrote(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(System.IO.Path.Combine(CausalLoopCorpus.SourceFolder, relative), TestContext.Current.CancellationToken);
        var model = CausalLoopBody.Parse(text).Read();
        var compared = 0;
        var differences = new List<string>();

        foreach ((string entry, Func<Task<ICommand?>> dispatch, ICommand? handWritten) in Cases(text, model))
        {
            // The hand-written provider threw before it dispatched (finding A7): nothing to compare.
            if (handWritten is null) continue;

            // Act.
            var dispatched = await dispatch() ?? throw new InvalidOperationException($"{entry} dispatched nothing.");
            var derived = await Written(text, dispatched);
            var expected = await Written(text, handWritten);

            // Assert.
            compared++;
            if (derived != expected) differences.Add($"{entry}:\nthe definition  {derived}\nthe hand-written {expected}");
        }

        Assert.True(differences.Count == 0, $"{relative}:\n{string.Join("\n\n", differences)}");
        Assert.True(compared >= 5, $"{relative}: only {compared} entries were compared.");
    }

    /// <summary>Every entry, on every element it applies to: how the host reaches it, and the command the hand-written provider dispatched for it.</summary>
    private static IEnumerable<(string Entry, Func<Task<ICommand?>> Dispatch, ICommand? HandWritten)> Cases(string text, CausalLoopModel model)
    {
        (string variableId, string variableLabel) = CausalLoopWriter.NextVariableName(model);
        yield return ("add variable", () => Executed(text, "", CausalLoopContextActionProvider.AddVariableActionId), new AddVariableCommand(Path, variableId, variableLabel));

        var names = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();
        foreach (var from in names)
        {
            var variable = $"variable:{from}";
            yield return Committed(text, model, variable, CausalLoopContextActionProvider.AddLoopActionId, "R77");
            foreach (var to in names.Append(Nobody))
            {
                yield return Committed(text, model, variable, CausalLoopContextActionProvider.AddLinkActionId, to);
                yield return Committed(text, model, GestureIds.Relation(from, to), CausalLoopContextActionProvider.ConnectActionId, "");
            }
        }

        yield return Committed(text, model, $"variable:{Nobody}", CausalLoopContextActionProvider.AddLinkActionId, names.FirstOrDefault() ?? Nobody);

        var links = model.Links.Select(link => (link.From, link.To, link.Polarity)).Append((Nobody, "nowhere", CausalLoopPolarity.Unstated));
        foreach ((string from, string to, CausalLoopPolarity polarity) in links)
        {
            var link = CausalLoopEdits.LinkId(from, to);
            yield return Committed(text, model, link, CausalLoopContextActionProvider.ToggleDelayActionId, "");
            yield return Committed(text, model, link, CausalLoopContextActionProvider.FlipCurvatureActionId, "");
            if (polarity != CausalLoopPolarity.Positive) yield return Operated("makePositive", link, new SetLinkPolarityCommand(Path, from, to, CausalLoopPolarity.Positive));
            if (polarity != CausalLoopPolarity.Negative) yield return Operated("makeNegative", link, new SetLinkPolarityCommand(Path, from, to, CausalLoopPolarity.Negative));
        }

        foreach (var loop in model.Loops)
        {
            yield return ($"renameLoop on {loop.Id}",
                () => Task.FromResult<ICommand?>(new RunCausalLoopOperationCommand(Path, "renameLoop", loop.Id, Parameters(("title", "Derived title")))),
                new SetLoopNameCommand(Path, loop.Identifier, "Derived title"));
        }
    }

    /// <summary>An entry committed with <paramref name="value"/> on <paramref name="elementId"/>, and the hand-written command for it; none where that threw (finding A7).</summary>
    private static (string, Func<Task<ICommand?>>, ICommand?) Committed(string text, CausalLoopModel model, string elementId, string actionId, string value)
    {
        ICommand? handWritten;
        try
        {
            handWritten = HandWrittenCausalLoop.Commit(model, Path, elementId, actionId, value);
        }
        catch (ArgumentException)
        {
            handWritten = null;
        }

        return ($"{actionId} on {elementId} = '{value}'", () => CommittedAsync(text, elementId, actionId, value), handWritten);
    }

    /// <summary>The definition's operation run on <paramref name="link"/>, and the record its menu entry dispatches.</summary>
    private static (string, Func<Task<ICommand?>>, ICommand?) Operated(string operation, string link, ICommand dispatched) =>
        ($"{operation} on {link}", () => Task.FromResult<ICommand?>(new RunCausalLoopOperationCommand(Path, operation, link, Parameters())), dispatched);

    private static Dictionary<string, string> Parameters(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    /// <summary>The command the action provider dispatches when <paramref name="actionId"/> is committed on <paramref name="elementId"/>.</summary>
    private static async Task<ICommand?> CommittedAsync(string text, string elementId, string actionId, string value)
    {
        (CausalLoopContextActionProvider actions, RecordingHistory history) = Provider(text);
        await actions.CommitAsync(Target(elementId), actionId, value, "", TestContext.Current.CancellationToken);
        return history.Take();
    }

    /// <summary>The command the action provider dispatches when <paramref name="actionId"/> is run on <paramref name="elementId"/>.</summary>
    private static async Task<ICommand?> Executed(string text, string elementId, string actionId)
    {
        (CausalLoopContextActionProvider actions, RecordingHistory history) = Provider(text);
        await actions.ExecuteAsync(Target(elementId), actionId, TestContext.Current.CancellationToken);
        return history.Take();
    }

    private static (CausalLoopContextActionProvider Actions, RecordingHistory History) Provider(string text)
    {
        var history = new RecordingHistory();
        return (new CausalLoopContextActionProvider(new CausalLoopMemoryStore { [Path] = text }, history, new DiagramViewportRegistry()), history);
    }

    private static ContextTarget Target(string elementId) => ContextTarget.ForElement(Path, elementId, origin: Diagram.CausalLoop.Origin);

    /// <summary>The text the command leaves, or its refusal, or what it threw.</summary>
    private static async Task<string> Written(string text, ICommand command)
    {
        var store = new CausalLoopMemoryStore { [Path] = text };
        var token = TestContext.Current.CancellationToken;
        try
        {
            var result = command switch
            {
                RunCausalLoopOperationCommand run => await new RunCausalLoopOperationCommandHandler(store).ExecuteAsync(run, token),
                AddVariableCommand add => await new AddVariableCommandHandler(store).ExecuteAsync(add, token),
                AddLoopCommand add => await new AddLoopCommandHandler(store).ExecuteAsync(add, token),
                AddLinkCommand add => await new AddLinkCommandHandler(store).ExecuteAsync(add, token),
                SetLinkPolarityCommand set => await new SetLinkPolarityCommandHandler(store).ExecuteAsync(set, token),
                SetLinkDelayCommand set => await new SetLinkDelayCommandHandler(store).ExecuteAsync(set, token),
                SetLinkCurvatureCommand set => await new SetLinkCurvatureCommandHandler(store).ExecuteAsync(set, token),
                SetLoopNameCommand set => await new SetLoopNameCommandHandler(store).ExecuteAsync(set, token),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, "No handler is wired for this command here."),
            };
            return result.IsSuccess ? store[Path] : $"refused: {result.Error}";
        }
        catch (ArgumentException exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }
}
