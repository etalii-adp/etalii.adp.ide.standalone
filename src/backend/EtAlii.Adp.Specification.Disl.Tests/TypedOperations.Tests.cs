using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Operations and actions are typed at load (FR-079), and typing them moves no outcome: what the runtime refused,
/// threw on or handed back when it read them from the JSON as they ran, it still does when they run, and loading
/// reports none of it.
/// </summary>
public class TypedOperationsTests
{
    private const string Parts = """
        "behavior": {
          "operations": {
            "notAnObject": { "for": "diagram", "actions": [ 3 ] },
            "noKind": { "for": "diagram", "actions": [ { "when": "true" } ] },
            "skippedNoKind": { "for": "diagram", "actions": [ { "when": "false" }, { "layout": { "algorithm": "tidy" } } ] },
            "setNotAnObject": { "for": "Thing", "actions": [ { "set": "self" } ] },
            "setNoTarget": { "for": "diagram", "actions": [ { "set": "self" } ] },
            "pluginNumber": { "for": "diagram", "actions": [ { "plugin": 7, "args": { "a": "'x'" } } ] },
            "literals": { "for": "diagram", "actions": [ { "view": { "tags": [ 1, 2 ] }, "target": "diagram.elementById('a')" } ] },
            "handedBack": { "for": "diagram", "plugin": { "name": "net.example.plugin", "args": { "list": [ 1 ] } } },
            "operationNotAnObject": 5
          }
        }
        """;

    private static DislSpecification Specification { get; } = Specifications.Loaded(Specifications.With(Parts));

    [Fact]
    public void AMalformedActionOrOperation_LoadsWithoutAnError()
    {
        Assert.Empty(DislLoader.Load(Specifications.With(Parts)).Errors);
    }

    [Fact]
    public void AnActionThatIsNotAnObject_OrOfNoKind_IsRefusedAsItRuns()
    {
        var diagram = new DislDiagram(Specification);

        Assert.Equal("The action at /behavior/operations/notAnObject/actions/0 is not an object.", OperationInterpreter.Run(Specification, "notAnObject", diagram, null, DislIds.Fixed()).Refusal);
        Assert.Equal("The action at /behavior/operations/noKind/actions/0 is none this runtime knows.", OperationInterpreter.Run(Specification, "noKind", diagram, null, DislIds.Fixed()).Refusal);
        Assert.Equal(new HostAction.Layout("tidy"), Assert.Single(OperationInterpreter.Run(Specification, "skippedNoKind", diagram, null, DislIds.Fixed()).HostActions));
    }

    [Fact]
    public void ABodyTheRuntimeThrewOn_StillThrowsWhenItRuns_AfterWhatCameBefore()
    {
        var diagram = new DislDiagram(Specification);
        var thing = diagram.AddNode("Thing", "a", new Dictionary<string, object?>());

        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Run(Specification, "setNotAnObject", diagram, thing, DislIds.Fixed()));
        Assert.Equal("The action at /behavior/operations/setNoTarget/actions/0 has no element to change.", OperationInterpreter.Run(Specification, "setNoTarget", diagram, null, DislIds.Fixed()).Refusal);
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Run(Specification, "pluginNumber", diagram, null, DislIds.Fixed()));
    }

    [Fact]
    public void AnOperationThatIsNotAnObject_AppliesToAnything_AndThrowsWhenItRuns()
    {
        var diagram = new DislDiagram(Specification);

        Assert.True(OperationInterpreter.AppliesTo(Specification, "operationNotAnObject", null));
        Assert.Throws<InvalidOperationException>(() => OperationInterpreter.Run(Specification, "operationNotAnObject", diagram, null, DislIds.Fixed()));
    }

    [Fact]
    public void ALiteralList_IsAListOfItsOwnOnEveryRun()
    {
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Thing", "a", new Dictionary<string, object?>());

        var first = (List<object?>)Assert.IsType<HostAction.View>(Assert.Single(OperationInterpreter.Run(Specification, "literals", diagram, null, DislIds.Fixed()).HostActions)).Values["tags"]!;
        first.Clear();
        var second = (List<object?>)Assert.IsType<HostAction.View>(Assert.Single(OperationInterpreter.Run(Specification, "literals", diagram, null, DislIds.Fixed()).HostActions)).Values["tags"]!;
        Assert.Equal("1 2", string.Join(" ", second));

        var handedBack = (List<object?>)Assert.IsType<HostAction.Plugin>(Assert.Single(OperationInterpreter.Run(Specification, "handedBack", diagram, null, DislIds.Fixed()).HostActions)).Arguments["list"]!;
        handedBack.Clear();
        var again = (List<object?>)Assert.IsType<HostAction.Plugin>(Assert.Single(OperationInterpreter.Run(Specification, "handedBack", diagram, null, DislIds.Fixed()).HostActions)).Arguments["list"]!;
        Assert.Equal("1", string.Join(" ", again));
    }
}
