using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The palette of each bundled definition (DISL §7.1, §7.2).</summary>
public class ToolboxDerivationTests
{
    private static string Line(DerivedTool tool) =>
        $"{tool.Group}/{tool.Name} {tool.Id} | {tool.Creates} | {tool.Label} | {tool.Icon} | drops {tool.DropActionId} | {tool.Description}";

    [Fact]
    public void TheHypeCycle_HasATrendATriggerAndANote()
    {
        var tools = ToolboxDerivation.Derive(Derivations.HypeCycle, Derivations.HypeCycleIds);

        Assert.Equal(
        [
            "hypeCycle/trend ghg.toolbox.trend | Trend | Trend | mdi-arrow-right-bold-box-outline | drops ghg.add.trend | A trend through the hype cycle. Drop it where it starts; it is a year long with all four phases.",
            "hypeCycle/trigger ghg.toolbox.trigger | Trigger | Trigger | mdi-circle-slice-8 | drops ghg.add.trigger | A moment in time that set trends off - an invention, a political moment, a disaster. Drop it where it happened; draw influences from it.",
            "hypeCycle/note ghg.toolbox.note | Note | Note | mdi-note-text-outline | drops ghg.add.note | A remark of your own, placed where it applies. Drop it and start typing.",
        ], tools.Select(Line));
    }

    [Fact]
    public void TheBehaviorModel_HasOneToolPerKind()
    {
        var tools = ToolboxDerivation.Derive(Derivations.BehaviorModel, Derivations.BehaviorModelIds);

        Assert.Equal(11, tools.Count);
        Assert.Equal(
            "nodes/sequence abm.toolbox.sequence | Sequence | Do in order | mdi-arrow-right-bold-outline | drops abm.add.sequence | Runs its children one after another, and fails as soon as one fails. Drop it below the node it belongs under.",
            Line(tools[0]));
        Assert.Equal(
            "nodes/delegate abm.toolbox.delegate | Delegate | Delegate | mdi-account-arrow-right-outline | drops abm.add.delegate | Hands its task to a sub-agent, or follows the behavior file it links to. Drop it below the node it belongs under.",
            Line(tools[^1]));
    }

    [Fact]
    public void WithoutAMap_TheIdsAreTheSpecificationsAndNothingDrops()
    {
        var tools = ToolboxDerivation.Derive(Derivations.HypeCycle, WireIdMap.None);

        Assert.Equal(["trend", "trigger", "note"], tools.Select(tool => tool.Id));
        Assert.All(tools, tool => Assert.Null(tool.DropActionId));
    }

    [Fact]
    public void ALabellessTool_IsLabelledAsTheTypeItCreates_AndALibraryToolIsFound()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "metamodel": { "types": { "Thing": { "label": "A thing", "icon": "mdi-cube", "attributes": { "name": { "type": "string" } } } } },
            "toolbox": {
              "tools": { "shared": { "creates": "Thing", "doc": { "summary": "Shared." } } },
              "groups": [ { "id": "outer", "tools": [ "shared" ], "groups": [ { "id": "inner", "tools": [ { "id": "own", "creates": "Thing", "label": { "cel": "'Own ' + string(size(diagram.nodes))" } } ] } ] } ]
            }
            """));

        var tools = ToolboxDerivation.Derive(specification, WireIdMap.None);

        Assert.Equal(["outer/shared shared | Thing | A thing | mdi-cube | drops  | Shared.", "inner/own own | Thing | Own 0 | mdi-cube | drops  | "], tools.Select(Line));
    }
}
