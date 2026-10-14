using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// Each operation of the bundled definition, run by the DISL interpreter with its <c>on: connect</c> hooks
/// and written through <see cref="CausalLoopDefinition.Apply"/>, writes the bytes the module's command for
/// that menu entry writes, or is refused with its sentence (causal-loop-disl-fbl Requirements 5.1, 5.5 and
/// 5.6): for every document of the parity corpus and every element an operation applies to.
/// </summary>
/// <remarks>
/// The commands are what the menus dispatch today; this holds them to the definition they are derived from,
/// so that what stays code - the checks before a write, the next free name - says what the definition says.
/// An operation the menu offers as unavailable, such as making a positive link positive, is not run.
/// </remarks>
public class CausalLoopDerivedOperationsTests
{
    private const string Path = "derived.cld";

    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryOperation_WritesWhatItsCommandWrites(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(System.IO.Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);
        var model = CausalLoopBody.Parse(text).Read();
        var compared = 0;
        var differences = new List<string>();

        foreach (var (operation, target, invocation, command) in Cases(model))
        {
            // Act.
            var derived = Derived(text, operation, target, invocation);
            var commanded = await Commanded(text, command);

            // Assert.
            if (derived is null) continue;
            compared++;
            if (derived != commanded) differences.Add($"{operation} on {target ?? "the diagram"}:\nthe definition {derived}\nthe command    {commanded}");
        }

        Assert.True(differences.Count == 0, $"{relative}:\n{string.Join("\n\n", differences)}");
        Assert.True(compared >= 5, $"{relative}: only {compared} operations were compared.");
    }

    /// <summary>Every operation, on every element it applies to, with the command its menu entry dispatches.</summary>
    private static IEnumerable<(string Operation, string? Target, DislInvocation? Invocation, ICommand Command)> Cases(CausalLoopModel model)
    {
        // addVariableHere is not compared: the definition's lowestFreeVariableNumber() filters a range of
        // 100,000 numbers and exceeds the definition's own cost limit on every document, so the interpreter
        // refuses it. The menu names the variable in code (CausalLoopEdits.NextVariableName) and is unaffected;
        // the correction belongs to the definition in etalii.adp.
        var names = model.Variables.Select(variable => variable.Id).Distinct(StringComparer.Ordinal).ToList();

        foreach (var from in names)
        {
            // A document whose variables share a name throws before the menu dispatches (finding A7), so it has no claim to compare.
            if (LoopThrough(model, from) is { } through) yield return ("claimLoop", $"variable:{from}", Parameters(("identifier", "R77")), new AddLoopCommand(Path, "R77", "R77", through));
            foreach (var to in names.Append("nobody"))
            {
                yield return ("addLink", $"variable:{from}", Parameters(("to", to)), new AddLinkCommand(Path, from, to, CausalLoopPolarity.Positive));
                if (to != "nobody") yield return ("connectLink", null, Parameters(("from", from), ("to", to)), new AddLinkCommand(Path, from, to, CausalLoopPolarity.Positive));
            }
        }

        foreach (var link in model.Links)
        {
            yield return ("makePositive", link.Id, null, new SetLinkPolarityCommand(Path, link.From, link.To, CausalLoopPolarity.Positive));
            yield return ("makeNegative", link.Id, null, new SetLinkPolarityCommand(Path, link.From, link.To, CausalLoopPolarity.Negative));
            yield return ("toggleDelay", link.Id, null, new SetLinkDelayCommand(Path, link.From, link.To, !link.Delayed));
            yield return ("flipCurve", link.Id, null, new SetLinkCurvatureCommand(Path, link.From, link.To, !link.Flipped));
        }

        foreach (var loop in model.Loops)
        {
            yield return ("renameLoop", loop.Id, Parameters(("title", "Derived title")), new SetLoopNameCommand(Path, loop.Identifier, "Derived title"));
        }
    }

    private static DislInvocation Parameters(params (string Name, object? Value)[] values) =>
        new(Parameters: values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal));

    /// <summary>
    /// The text the operation leaves, or its refusal, or what it threw; null for an operation the menu
    /// offers as unavailable, or for an element the document does not hold.
    /// </summary>
    private static string? Derived(string text, string operation, string? target, DislInvocation? invocation)
    {
        var body = CausalLoopBody.Parse(text);
        try
        {
            var diagram = body.Working();
            var self = target is null ? null : diagram.ElementById(target);
            if (target is not null && self is null) return null;
            if (invocation?.Parameters is { } parameters && operation == "connectLink")
            {
                // The gesture names its ends by the variables written with those names.
                invocation = invocation with
                {
                    Parameters = parameters.ToDictionary(parameter => parameter.Key, parameter => (object?)CausalLoopDefinition.VariableOf(diagram, (string)parameter.Value!), StringComparer.Ordinal),
                };
            }

            var ids = new Ids();
            var transaction = OperationInterpreter.Run(CausalLoopDefinition.Specification, operation, diagram, self, ids, invocation, CausalLoopDefinition.EditEnv);
            if (!transaction.WasApplied)
            {
                return OperationInterpreter.AppliesTo(CausalLoopDefinition.Specification, operation, self) && IsUnavailable(operation, self)
                    ? null
                    : $"refused: {transaction.Refusal}";
            }

            // The host runs the hooks of every relation an operation drew, in the same edit.
            var changes = new List<DislChange>(transaction.Changes);
            foreach (var connect in transaction.Changes.OfType<DislChange.Connect>())
            {
                var hooked = HookRunner.AfterConnect(CausalLoopDefinition.Specification, diagram.ElementById(connect.Id)!, ids, CausalLoopDefinition.EditEnv);
                if (!hooked.WasApplied) return $"refused: {hooked.Refusal}";
                changes.AddRange(hooked.Changes);
            }

            var outcome = CausalLoopDefinition.Apply(body, new DislTransaction(changes, [], null));
            return outcome.WasApplied ? body.Text : $"refused: {outcome.Refusal}";
        }
        catch (ArgumentException exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    /// <summary>Whether the menu offers <paramref name="operation"/> on <paramref name="self"/> as unavailable: an <c>unavailable</c> reason holds.</summary>
    private static bool IsUnavailable(string operation, DislElement? self) => operation switch
    {
        "makePositive" => self?.Attributes.GetValueOrDefault("polarity") as string == "positive",
        "makeNegative" => self?.Attributes.GetValueOrDefault("polarity") as string == "negative",
        _ => false,
    };

    /// <summary>The text the command leaves, or its refusal, or what it threw.</summary>
    private static async Task<string> Commanded(string text, ICommand command)
    {
        var store = new CausalLoopMemoryStore { [Path] = text };
        var token = TestContext.Current.CancellationToken;
        try
        {
            var result = command switch
            {
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

    /// <summary>The shortest cycle through a variable, or the variable alone, as the provider proposes a claimed loop; null where the provider throws.</summary>
    private static List<string>? LoopThrough(CausalLoopModel model, string variable)
    {
        try
        {
            return CycleFinder.Find(model).Cycles
                .Where(cycle => cycle.Contains(variable, StringComparer.Ordinal))
                .OrderBy(cycle => cycle.Count)
                .FirstOrDefault()?.ToList() ?? [variable];
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Ids for what an operation creates: unique on the working diagram, and never written.</summary>
    private sealed class Ids : IIdSource
    {
        private int _next;

        public string Next(string type) => string.Create(CultureInfo.InvariantCulture, $"{type}:derived-{++_next}");
    }
}
