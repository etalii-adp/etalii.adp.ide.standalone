using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The <c>connect</c> and <c>retype</c> actions of an operation (DISL §9.4), and a connect gesture released on
/// empty canvas creating its missing end through its tool's <c>createTarget</c> or <c>createSource</c> (§6.10, §7.2).
/// </summary>
public class ConnectAndRetypeActionsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": {
            "Item": { "abstract": true, "attributes": { "name": { "type": "string" }, "row": { "type": "int" } } },
            "Span": { "extends": ["Item"], "attributes": { "end": { "type": "string" } } },
            "Point": { "extends": ["Item"] }
          },
          "relations": { "Next": { "source": "Item", "target": "Item", "attributes": { "label": { "type": "string" } } } }
        },
        "notation": { "edges": { "Next": { "connect": { "from": { "right": "source", "left": "target" }, "tool": "relate" } } } },
        "toolbox": {
          "tools": {
            "relate": { "id": "relate", "creates": "Next", "mode": "drag",
              "createTarget": { "type": "Span", "initial": { "name": "New", "row": { "cel": "int(position.y)" } } },
              "createSource": "Point" }
          }
        },
        "behavior": {
          "operations": {
            "grow": { "label": "Grow", "for": ["Item"], "actions": [
              { "create": { "type": "'Span'", "attributes": { "name": "'Next'", "row": "self.row + 1" } }, "as": "next" },
              { "connect": { "type": "'Next'", "source": "self", "target": "next", "attributes": { "label": "'after ' + self.name" } }, "as": "link" },
              { "set": { "label": "link.label + '!'" }, "target": "link" } ] },
            "widen": { "label": "Widen", "for": ["Point"], "actions": [ { "retype": { "to": "'Span'" } }, { "set": { "end": "'later'" } } ] },
            "narrow": { "label": "Narrow", "for": ["Span"], "actions": [ { "unset": ["end"] }, { "retype": { "to": "'Point'" } }, { "set": { "name": "self.type" } } ] },
            "nowhere": { "label": "Nowhere", "for": ["Span"], "actions": [ { "retype": { "to": "'Item'" } } ] }
          },
          "retype": { "Point": { "to": ["Span"] }, "Span": { "to": ["Point"] } }
        }
        """));

    private static string Text(DislTransaction transaction) =>
        (transaction.Refusal is { } refusal ? $"refused: {refusal}" : "")
        + string.Join("\n", transaction.Changes.Select(change => change switch
        {
            DislChange.Create create => $"create {create.Type} {create.Id} {Values(create.Attributes)}",
            DislChange.Connect connect => $"connect {connect.Type} {connect.Id} {connect.SourceId}->{connect.TargetId} {Values(connect.Attributes)}",
            DislChange.Set set => $"set {set.ElementId} {set.Type} {Values(set.Attributes)}",
            DislChange.Retype retype => $"retype {retype.ElementId} {retype.Type}",
            _ => change.ToString(),
        }));

    private static string Values(IReadOnlyDictionary<string, object?> values) =>
        string.Join(" ", values.Select(pair => $"{pair.Key}={pair.Value ?? "null"}"));

    [Fact]
    public void AConnect_CreatesTheRelationBetweenItsEnds_AndNamesItForTheActionsAfter()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Point", "a", new Dictionary<string, object?> { ["name"] = "A", ["row"] = 2L });

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "grow", diagram, a, DislIds.Fixed("n1", "r1"));

        // Assert.
        Assert.Equal(
            "create Span n1 name=Next row=3\nconnect Next r1 a->n1 label=after A\nset r1 Next label=after A!",
            Text(transaction));
        var relation = Assert.Single(diagram.Relations);
        Assert.Same(a, relation.Source);
        Assert.Equal("n1", relation.Target?.Id);
    }

    [Fact]
    public void ARetype_ChangesTheTypeForTheActionsAfterIt()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var point = diagram.AddNode("Point", "p", new Dictionary<string, object?> { ["name"] = "P" });

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "widen", diagram, point, DislIds.Fixed());

        // Assert.
        Assert.Equal("retype p Span\nset p Span end=later", Text(transaction));
        Assert.True(point.IsA("Span"));
        Assert.Equal("P", point.ValueOf("name"));
    }

    [Fact]
    public void ARetype_ForgetsWhatTheNewTypeDoesNotDeclare()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var span = diagram.AddNode("Span", "s", new Dictionary<string, object?> { ["name"] = "S", ["end"] = "x" });

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "narrow", diagram, span, DislIds.Fixed());

        // Assert.
        Assert.Equal("set s Span end=null\nretype s Point\nset s Point name=Point", Text(transaction));
        Assert.False(span.Attributes.ContainsKey("end"));
    }

    [Fact]
    public void ARetypeTheDefinitionDoesNotAllow_RefusesTheTransaction()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var span = diagram.AddNode("Span", "s");

        // Act.
        var transaction = OperationInterpreter.Run(Specification, "nowhere", diagram, span, DislIds.Fixed());

        // Assert.
        Assert.Equal("refused: A Span cannot become a Item: that is no type of node.", Text(transaction));
    }

    [Fact]
    public void AGestureReleasedOnEmptyCanvas_CreatesTheTargetFromItsTool_ThenTheRelation()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Point", "a");

        // Act.
        var transaction = OperationInterpreter.ConnectToNew(
            Specification, "Next", diagram, a, "target", new Dictionary<string, object?> { ["x"] = 1.0, ["y"] = 4.0 }, DislIds.Fixed("n1", "r1"));

        // Assert.
        Assert.Equal("create Span n1 name=New row=4\nconnect Next r1 a->n1 ", Text(transaction));
    }

    [Fact]
    public void AGestureFromATargetAnchor_CreatesTheSource_PointingIntoTheExistingElement()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Span", "a");

        // Act.
        var transaction = OperationInterpreter.ConnectToNew(
            Specification, "Next", diagram, a, "source", new Dictionary<string, object?> { ["x"] = 1.0, ["y"] = 4.0 }, DislIds.Fixed("n1", "r1"));

        // Assert.
        Assert.Equal("create Point n1 \nconnect Next r1 n1->a ", Text(transaction));
    }

    [Theory]
    [InlineData("widen", "Point", true)]
    [InlineData("widen", "Span", false)]
    [InlineData("grow", "Span", true)]
    [InlineData("absent", "Span", false)]
    public void AnOperation_AppliesToTheTypesItsForNames(string operation, string type, bool expected)
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var element = diagram.AddNode(type, "e");

        // Act.
        var applies = OperationInterpreter.AppliesTo(Specification, operation, element);

        // Assert.
        Assert.Equal(expected, applies);
    }
}
