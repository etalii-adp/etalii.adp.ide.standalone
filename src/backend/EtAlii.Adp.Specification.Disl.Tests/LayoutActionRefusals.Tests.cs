using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A <c>layout</c> action's <c>refusals</c> (DISL 0.3 §9.4): the host that runs the algorithm is handed the sentence
/// for a scope with nothing to place and the one for a layout that changes nothing, evaluated for the action.
/// </summary>
public class LayoutActionRefusalsTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "behavior": { "operations": {
          "tidy": { "label": "Tidy", "for": "diagram", "actions": [
            { "layout": { "algorithm": "rows", "refusals": {
              "nothingDrawn": "There is nothing to tidy.",
              "unchanged": { "cel": "'These ' + string(size(diagram.nodes)) + ' are already tidy.'" } } } } ] },
          "plain": { "label": "Plain", "for": "diagram", "actions": [ { "layout": { "algorithm": "rows" } } ] } } }
        """));

    private static HostAction.Layout Run(string operation)
    {
        var diagram = new DislDiagram(Specification);
        diagram.AddNode("Thing", "a");
        diagram.AddNode("Thing", "b");
        return Assert.IsType<HostAction.Layout>(Assert.Single(OperationInterpreter.Run(Specification, operation, diagram, null, DislIds.Fixed()).HostActions));
    }

    [Fact]
    public void TheRefusals_AreHandedToTheHost_Evaluated()
    {
        // Act.
        var layout = Run("tidy");

        // Assert.
        Assert.Equal(("rows", "There is nothing to tidy.", "These 2 are already tidy."), (layout.Algorithm, layout.NothingDrawn, layout.Unchanged));
    }

    [Fact]
    public void ALayoutWithoutRefusals_HandsNone()
    {
        // Act.
        var layout = Run("plain");

        // Assert.
        Assert.Equal(new HostAction.Layout("rows"), layout);
        Assert.Null(layout.NothingDrawn);
        Assert.Null(layout.Unchanged);
    }
}
