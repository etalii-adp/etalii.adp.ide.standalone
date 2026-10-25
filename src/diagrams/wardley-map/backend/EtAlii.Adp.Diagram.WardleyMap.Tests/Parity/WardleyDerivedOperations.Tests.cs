using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// Every operation of the definition maps to a command, and to the hand-written one (wardley-map-disl-fbl task 6): for
/// every document of the parity corpus, every operation on every element it is for (the map's on the map), with each
/// value its parameters can take, run by <see cref="OperationInterpreter"/>, comes to the command
/// <see cref="WardleyTransactionCommands"/> gives it, and that command is the one the hand-written action with the same
/// id makes from the same value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the definition refuses</b>, the hand-written action made its command anyway and its handler refused it
/// (<see cref="DiagramAction{TSubject}"/>): such a command is dispatched, and its refusal must be the definition's.
/// </para>
/// <para>
/// <b>The values</b>: a name is <c>Parity value</c>, a target maturity <c>0.5</c>, a decorator each of the five and a
/// link each link of the element, which the hand-written action is given by its choice id. Commands are compared as
/// records, by value.
/// </para>
/// <para>
/// <b>What the bytes come to</b> is the parity transcript's to show, which dispatches every one of these and records
/// the hunks; this test runs every element of the corpus rather than the transcript's scripted few.
/// </para>
/// </remarks>
public class WardleyDerivedOperationsTests
{
    private const string Name = "Parity value";
    private const string Maturity = "0.5";

    private static readonly string[] Decorators = ["market", "ecosystem", "build", "buy", "outsource"];

    public static TheoryData<string> Documents() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryOperation_MapsToTheHandWrittenCommand(string name)
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
        var entry = session.Store.Get(session.Body)!;
        if (!entry.IsUsable)
        {
            return;
        }

        var identities = session.Store.Identities(session.Body);

        // Act.
        List<string> mismatches = [];
        foreach (var step in Steps(WardleyDisl.Of(entry.Document, entry.Model, identities)))
        {
            var working = WardleyDisl.Build(entry.Document, entry.Model, identities);
            var self = step.SelfId is null ? null : working.ElementById(step.SelfId);
            var parameters = step.Parameters.ToDictionary(
                parameter => parameter.Key,
                parameter => parameter.Value is DislElement element ? working.ElementById(element.Id) : parameter.Value,
                StringComparer.Ordinal);
            var transaction = OperationInterpreter.Run(
                WardleyDefinition.Specification, step.Operation, working, self, DislIds.Of(WardleyDefinition.Specification), new DislInvocation(Parameters: parameters), WardleyDefinition.Env);

            var subject = WardleySubject.Of(session.Body, entry.Model, session.Store, step.SelfId ?? "");
            var handWritten = HandWrittenWardleyActions.All.Applying(step.ActionId, subject);
            if (!transaction.WasApplied)
            {
                // The hand-written action made its command and left the refusal to its handler; the definition refuses
                // before there is a command, in the same sentence.
                if (handWritten?.Command(subject, step.Value) is { } offered
                    && await session.Dispatcher.DispatchAsync(offered, cancellationToken) is var dispatched
                    && (dispatched.IsSuccess || dispatched.Error != transaction.Refusal))
                {
                    mismatches.Add($"{step}: refused ({transaction.Refusal}), where the hand-written command {(dispatched.IsSuccess ? "succeeds" : $"is refused ({dispatched.Error})")}");
                }

                continue;
            }

            var command = WardleyTransactionCommands.Of(session.Body, WardleyDisl.Of(entry.Document, entry.Model, identities), transaction);
            if (command is null)
            {
                mismatches.Add($"{step}: no command writes {string.Join(", ", transaction.Changes)}");
                continue;
            }

            var expected = handWritten?.Command(subject, step.Value);
            if (!Equals(expected, command))
            {
                mismatches.Add($"{step}:\n  expected {expected?.ToString() ?? "no command"}\n  actual   {command}");
            }
        }

        // Assert.
        Assert.True(mismatches.Count == 0, $"{name}:\n{string.Join("\n", mismatches)}");
    }

    [Fact]
    public async Task EveryOperation_RunsSomewhereInTheCorpus()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var unrun = new HashSet<string>(Operations(), StringComparer.Ordinal);

        // Act.
        foreach (var name in WardleyCorpus.Names())
        {
            using var session = await WardleyCorpus.OpenAsync(name, cancellationToken);
            var entry = session.Store.Get(session.Body)!;
            if (!entry.IsUsable)
            {
                continue;
            }

            var identities = session.Store.Identities(session.Body);
            foreach (var step in Steps(WardleyDisl.Of(entry.Document, entry.Model, identities)).Where(step => unrun.Contains(step.Operation)))
            {
                var working = WardleyDisl.Build(entry.Document, entry.Model, identities);
                var self = step.SelfId is null ? null : working.ElementById(step.SelfId);
                var parameters = step.Parameters.ToDictionary(
                    parameter => parameter.Key,
                    parameter => parameter.Value is DislElement element ? working.ElementById(element.Id) : parameter.Value,
                    StringComparer.Ordinal);
                if (OperationInterpreter.Run(WardleyDefinition.Specification, step.Operation, working, self, DislIds.Of(WardleyDefinition.Specification), new DislInvocation(Parameters: parameters), WardleyDefinition.Env).WasApplied)
                {
                    unrun.Remove(step.Operation);
                }
            }
        }

        // Assert: none of them is shown to map only because the corpus never runs it.
        Assert.Empty(unrun);
    }

    /// <summary>Every operation on every element it is for, the map's on the map, with each value its parameters take.</summary>
    private static IEnumerable<Step> Steps(DislDiagram diagram)
    {
        var specification = WardleyDefinition.Specification;
        IEnumerable<DislElement?> selves = [null, .. diagram.Elements.Where(element => element.IsA("Positioned") && ReferenceEquals(WardleyDisl.ElementOf(diagram, element.Id), element))];
        foreach (var self in selves)
        {
            IReadOnlyList<string> types = self?.Type.Linearisation ?? ["diagram"];
            foreach (var operation in Operations().Where(operation => OperationInterpreter.AppliesTo(specification, operation, self)))
            {
                foreach (var (parameters, value) in Values(diagram, operation, self))
                {
                    var bindings = operation == "toggleDecorator" ? parameters : new Dictionary<string, object?>(StringComparer.Ordinal);
                    yield return new Step(operation, self?.Id, WardleyDefinition.Definition.Ids.ActionId(types, "operation", operation, bindings), parameters, value);
                }
            }
        }
    }

    /// <summary>Each set of parameters <paramref name="operation"/> is run with, and the value the hand-written action is given for it.</summary>
    private static IEnumerable<(IReadOnlyDictionary<string, object?> Parameters, string Value)> Values(DislDiagram diagram, string operation, DislElement? self)
    {
        switch (operation)
        {
            case "toggleDecorator":
                foreach (var decorator in Decorators)
                {
                    yield return (Parameters(("decorator", decorator)), "");
                }

                break;

            case "unlink" when self is not null:
                foreach (var link in diagram.Elements.Where(element => element.IsA("Link") && (element.Source?.Id == self.Id || element.Target?.Id == self.Id)))
                {
                    yield return (Parameters(("link", link)), WardleyDisl.ChoiceIdOf(link));
                }

                break;

            case "evolve":
                yield return (Parameters(("maturity", Maturity)), Maturity);
                break;

            case "stopEvolving" or "toggleInertia":
                yield return (Parameters(), "");
                break;

            default:
                yield return (Parameters(("name", Name)), Name);
                break;
        }
    }

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] parameters) =>
        parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);

    private static IEnumerable<string> Operations() =>
        WardleyDefinition.Specification.Json.GetProperty("behavior").GetProperty("operations").EnumerateObject().Select(operation => operation.Name);

    private sealed record Step(string Operation, string? SelfId, string ActionId, IReadOnlyDictionary<string, object?> Parameters, string Value)
    {
        public override string ToString() => $"{Operation} on {SelfId ?? "the map"} as {ActionId}{(Value.Length > 0 ? $" = '{Value}'" : "")}";
    }
}
