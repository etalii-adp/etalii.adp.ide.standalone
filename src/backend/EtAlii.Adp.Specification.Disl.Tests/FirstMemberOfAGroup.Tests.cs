using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A custom constraint reported once per group at its first member (DISL §8.4, §12.4), with no option for it: its
/// <c>when</c> selects the first member by <c>positionIn</c>, and its message names every member's line by
/// <c>location()</c>.
/// </summary>
public class FirstMemberOfAGroupTests
{
    private const string Parts = """
        "disl": "0.3",
        "metamodel": { "types": { "Item": { "attributes": { "name": { "type": "string" } } } } },
        "functions": {
          "holders": { "params": [ { "name": "n", "type": "Element" } ], "returns": "list(Element)", "uses": ["diagram"],
            "cel": "diagram.nodesOfType('Item').filter(m, m.name == n.name)" }
        },
        "constraints": { "rules": [
          { "id": "unique", "code": "item.repeated", "scope": "Item",
            "when": "self.positionIn(holders(self)) == 0",
            "rule": "holders(self).size() == 1",
            "message": { "cel": "self.name + ' (lines ' + holders(self).map(n, string(n.location().orValue({'line': 0})['line'])).join(', ') + ')'" } } ] }
        """;

    [Fact]
    public void ARepeatedName_IsOneFinding_AtItsFirstHolder_NamingEveryHoldersLine()
    {
        var specification = Specifications.Loaded(Specifications.With(Parts));
        var diagram = new DislDiagram(specification) { File = "items.txt" };
        var line = 1;
        foreach ((string id, string name) in new[] { ("a", "Tea"), ("b", "Cup"), ("c", "Tea"), ("d", "Tea"), ("e", "Cup"), ("f", "Kettle") })
        {
            diagram.Add(new DislElement(diagram, specification.Metamodel.TypeOf("Item")!, id, new Dictionary<string, object?> { ["name"] = name }) { Line = line++ });
        }

        var findings = ConstraintEvaluator.Evaluate(specification, diagram).Where(finding => finding.Code == "item.repeated").Select(finding => $"{finding.Line} | {string.Join(",", finding.ElementIds)} | {finding.Message}");

        Assert.Equal(["1 | a | Tea (lines 1, 3, 4)", "2 | b | Cup (lines 2, 5)"], findings);
    }
}
