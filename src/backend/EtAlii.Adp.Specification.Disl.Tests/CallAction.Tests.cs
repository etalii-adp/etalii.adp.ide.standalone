using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A <c>call</c> action (DISL §9.4, <c>{ "call": "operationId", "args": { … } }</c>): the operation it names run in the
/// same transaction, with its <c>args</c> as <c>p</c> and the caller's <c>self</c>, refused as that operation refuses.
/// </summary>
public class CallActionTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": { "types": { "Box": { "attributes": {
          "name": { "type": "string" },
          "locked": { "type": "bool" } } } } },
        "behavior": { "operations": {
          "rename": { "for": ["Box"], "params": { "name": { "type": "string" } },
            "unavailable": [ { "when": "self.locked", "message": "This box is locked." } ],
            "actions": [
              { "if": "p.name.trim() == ''", "then": [ { "abort": { "message": "'A box needs a name.'" } } ] },
              { "set": { "name": "p.name.trim()" } } ] },
          "rename twice": { "for": ["Box"], "params": { "name": { "type": "string" } },
            "actions": [ { "call": "rename", "args": { "name": "p.name + '!'" } } ] },
          "loop": { "for": ["Box"], "actions": [ { "call": "loop" } ] } } },
        "forms": { "box": { "for": "Box", "items": [
          { "attribute": "name", "label": "Name", "parse": { "write": [ { "call": "rename", "args": { "name": "value" } } ] } } ] } }
        """));

    private static DislElement Box(bool locked = false)
    {
        var diagram = new DislDiagram(Specification);
        return diagram.AddNode("Box", "b", new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "old", ["locked"] = locked });
    }

    [Fact]
    public void ARowsParse_ThatCallsAnOperation_IsWrittenByThatOperationsActions()
    {
        var transaction = FormCommit.Row(Specification, Box(), "name", " new ", new DislEnv());

        Assert.Null(transaction.Refusal);
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal("new", set.Attributes["name"]);
    }

    [Fact]
    public void ACalledOperation_RefusesWithItsAbort_AndWithItsUnavailableReason()
    {
        Assert.Equal("A box needs a name.", FormCommit.Row(Specification, Box(), "name", " ", new DislEnv()).Refusal);
        Assert.Equal("This box is locked.", FormCommit.Row(Specification, Box(locked: true), "name", "new", new DislEnv()).Refusal);
    }

    [Fact]
    public void AnOperationThatCallsAnother_RunsItWithTheArgsAsItsParameters()
    {
        var box = Box();
        var transaction = OperationInterpreter.Run(Specification, "rename twice", box.Diagram, box, DislIds.Fixed(),
            new DislInvocation(Parameters: new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "new" }));

        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal("new!", set.Attributes["name"]);
    }

    [Fact]
    public void AnOperationThatCallsItself_IsRefused_RatherThanRunForever()
    {
        var box = Box();

        var transaction = OperationInterpreter.Run(Specification, "loop", box.Diagram, box, DislIds.Fixed());

        Assert.NotNull(transaction.Refusal);
    }
}
