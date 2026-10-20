using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A drop target is a context tool plus <c>on</c> (DISL §7.2): its <c>args</c> are Expressions as a context tool's are
/// (§7.3), of the element context with <c>self</c> the drop target and <c>position</c> the drop point, compiled at load.
/// </summary>
public class DropTargetLoadingTests
{
    private static string Dropping(string key) => Specifications.With($$"""
        "metamodel": { "types": { "Box": { "attributes": { "name": { "type": "string" } } } } },
        "toolbox": { "groups": [ { "id": "all", "tools": [
          { "id": "box", "kind": "operation", "operation": "addBox", "mode": "drop",
            "drop": { "targets": [ { "on": ["canvas"], "kind": "operation", "operation": "addBox",
              "args": { "name": "{{key}}" } } ] } } ] } ] },
        "behavior": { "operations": { "addBox": {
          "label": "Add box", "for": "diagram",
          "params": { "name": { "type": "string" } },
          "actions": [ { "create": { "type": "'Box'", "at": "position", "attributes": { "name": "p.name" } } } ] } } }
        """);

    [Fact]
    public void ADropTargetsArgs_AreCompiledInTheElementContext()
    {
        var specification = Specifications.Loaded(Dropping("'box ' + string(diagram.nodesOfType('Box').size() + 1)"));

        Assert.Equal(DislContext.Element, specification.ExpressionAt("/toolbox/groups/0/tools/0/drop/targets/0/args/name")?.Context);
    }

    [Fact]
    public void ADropTargetsArgs_SeeTheDropPoint()
    {
        Assert.Empty(Specifications.Diagnostics(Dropping("position == null ? 'box' : 'placed box'")));
    }

    [Fact]
    public void AMistakeInADropTargetsArgs_IsRefusedAtLoad()
    {
        var result = DislLoader.Load(Dropping("notAFunction(diagram)"));

        Assert.Null(result.Specification);
        Assert.Equal("/toolbox/groups/0/tools/0/drop/targets/0/args/name", Assert.Single(result.Errors).Pointer);
    }
}
