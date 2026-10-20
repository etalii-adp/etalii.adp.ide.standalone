using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The views of a model (DISL §12.2, <c>diagram.views</c>) and their members (§3.5): what a viewpoint's
/// <c>include</c>, <c>exclude</c> and <c>members</c> select, under the view's own diagram attributes and
/// viewpoint, and a rule over the model that asks about them (§8.2).
/// </summary>
public class ModelViewsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "diagram": { "attributes": { "includes": { "type": "string", "many": true }, "focus": { "type": "Box" } } },
          "types": {
            "Box": { "attributes": { "name": { "type": "string" } } },
            "Note": { "attributes": { "text": { "type": "string" } } } },
          "relations": { "Link": { "source": "Box", "target": "Box" } } },
        "viewpoints": {
          "chosen": { "include": ["Box", "Link"], "members": "self.kind == 'relation' || diagram.includes.exists(i, i == '*' || i == self.id)" },
          "around": { "exclude": ["Note"], "members": "env.viewpoint == 'around' && (self.kind == 'relation' || self == diagram.focus)" } },
        "constraints": { "rules": [
          { "id": "onNoView", "over": "model", "scope": "Box", "when": "diagram.views.size() > 0",
            "rule": "diagram.views.exists(v, v.members.exists(m, m == self))", "message": { "cel": "self.id + ' is on no view'" } } ] }
        """));

    private static DislDiagram Diagram()
    {
        var diagram = new DislDiagram(Specification, new Dictionary<string, object?> { ["includes"] = new List<object?> { "*" } });
        var a = diagram.AddNode("Box", "a");
        var b = diagram.AddNode("Box", "b");
        diagram.AddNode("Box", "c");
        diagram.AddNode("Note", "n");
        diagram.AddRelation("Link", "l", a, b);
        return diagram;
    }

    private static object? Evaluate(DislDiagram diagram, string expression) =>
        Specification.Environment(DislContext.Constraint).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = diagram, ["diagram"] = diagram, ["env"] = new CelMap() });

    [Fact]
    public void TheViewsOfTheModel_AreWhatTheHostAdded()
    {
        // Arrange.
        var diagram = Diagram();
        diagram.AddView("everything", "chosen", "Everything");
        diagram.AddView("b-only", "around", attributes: new Dictionary<string, object?> { ["focus"] = "b" });

        // Act.
        var ids = Evaluate(diagram, "diagram.views.map(v, v.id + ':' + v.viewpoint + ':' + v.name)");

        // Assert.
        Assert.Equal(new List<object?> { "everything:chosen:Everything", "b-only:around:" }, ids);
        Assert.Equal(["everything:chosen:Everything", "b-only:around:"], diagram.Views.Select(view => view.Id + ":" + view.Viewpoint + ":" + view.Name));
    }

    [Fact]
    public void AViewsMembers_FollowItsViewpoint_UnderItsOwnAttributes()
    {
        // Arrange.
        var diagram = Diagram();
        var everything = diagram.AddView("everything", "chosen");
        var chosen = diagram.AddView("chosen", "chosen", attributes: new Dictionary<string, object?> { ["includes"] = new List<object?> { "a", "c" } });
        var around = diagram.AddView("around", "around", attributes: new Dictionary<string, object?> { ["focus"] = "b" });

        // Act.
        var members = Evaluate(diagram, "diagram.views.map(v, v.members.map(m, m.id))");

        // Assert.
        Assert.Equal(["a", "b", "c", "l"], everything.Members.Select(member => member.Id));
        Assert.Equal(["a", "c", "l"], chosen.Members.Select(member => member.Id));
        Assert.Equal(["b", "l"], around.Members.Select(member => member.Id));
        Assert.Equal(new List<object?> { new List<object?> { "a", "b", "c", "l" }, new List<object?> { "a", "c", "l" }, new List<object?> { "b", "l" } }, members);
    }

    [Fact]
    public void ARuleOverTheModel_AsksAboutEveryView()
    {
        // Arrange.
        var diagram = Diagram();
        diagram.AddView("chosen", "chosen", attributes: new Dictionary<string, object?> { ["includes"] = new List<object?> { "a" } });
        diagram.AddView("around", "around", attributes: new Dictionary<string, object?> { ["focus"] = "b" });

        // Act.
        var findings = ConstraintEvaluator.Evaluate(Specification, diagram);

        // Assert.
        Assert.Equal(["c is on no view"], findings.Select(finding => finding.Message));
    }

    [Fact]
    public void AViewOfAViewpointTheSpecificationDoesNotDeclare_HasTheMembersTheHostGave()
    {
        // Arrange.
        var diagram = Diagram();
        var given = diagram.AddView("given", "elsewhere", members: [diagram.ElementById("c")!]);
        var unknown = diagram.AddView("unknown", "elsewhere");

        // Act.
        var read = Evaluate(diagram, "diagram.views[1].members");

        // Assert.
        Assert.Equal(["c"], given.Members.Select(member => member.Id));
        Assert.IsType<CelError>(read);
        Assert.Throws<CelException>(() => unknown.Members);
    }

    [Fact]
    public void AViewsMembers_AreComputedAgainWhenTheModelChanges()
    {
        // Arrange.
        var diagram = Diagram();
        var everything = diagram.AddView("everything", "chosen");
        Assert.Equal(4, everything.Members.Count);

        // Act.
        diagram.AddNode("Box", "d");

        // Assert.
        Assert.Equal(["a", "b", "c", "d", "l"], everything.Members.Select(member => member.Id));
    }
}
