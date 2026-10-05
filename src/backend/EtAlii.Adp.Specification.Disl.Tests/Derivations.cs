namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The two bundled definitions the derivation tests run against, loaded once, and their wire-id maps.</summary>
internal static class Derivations
{
    public static DislSpecification HypeCycle { get; } = BundledDefinition.Load(Resources.Assembly, Resources.HypeCycle).Specification;

    public static DislSpecification BehaviorModel { get; } = BundledDefinition.Load(Resources.Assembly, Resources.BehaviorModel).Specification;

    public static WireIdMap HypeCycleIds { get; } = WireIdMap.Of(HypeCycle, "x-ghg");

    public static WireIdMap BehaviorModelIds { get; } = WireIdMap.Of(BehaviorModel, "x-abm");

    /// <summary>A hype cycle graph: two trends (the second with a dragged peak end), a trigger, a note and three influences, one from a hidden phase.</summary>
    public static DislDiagram HypeCycleDiagram()
    {
        var diagram = new DislDiagram(HypeCycle);
        var steam = diagram.AddNode("Trend", "steam", new Dictionary<string, object?>
        {
            ["name"] = "Steam engine", ["start"] = 1800L * 12, ["stop"] = 1850L * 12, ["phases"] = 4L, ["tags"] = new List<object?> { "energy", "industry" },
        });
        var rail = diagram.AddNode("Trend", "rail", new Dictionary<string, object?>
        {
            ["name"] = "Railways", ["start"] = 1820L * 12, ["stop"] = 1900L * 12, ["phases"] = 2L, ["peakEnd"] = 1830L * 12, ["tags"] = new List<object?> { "transport" },
        });
        var watt = diagram.AddNode("Trigger", "watt", new Dictionary<string, object?> { ["name"] = "Watt's patent", ["date"] = 1769L * 12, ["tags"] = new List<object?> { "industry", "law" } });
        diagram.AddNode("Note", "remark", new Dictionary<string, object?> { ["text"] = "A remark", ["at"] = 1810L * 12, ["width"] = 160.0, ["height"] = 64.5 });
        diagram.AddRelation("Influence", "i1", steam, rail, new Dictionary<string, object?>
        {
            ["fromPhase"] = "plateau", ["fromEdge"] = "top", ["fromAt"] = 0.5, ["toPhase"] = "trough", ["toEdge"] = "bottom", ["toAt"] = 0.25,
        });
        diagram.AddRelation("Influence", "i2", watt, steam, new Dictionary<string, object?> { ["toPhase"] = "peak", ["toEdge"] = "top", ["toAt"] = 1.0 });
        diagram.AddRelation("Influence", "i3", rail, steam, new Dictionary<string, object?>
        {
            ["fromPhase"] = "slope", ["fromEdge"] = "bottom", ["fromAt"] = 0.75, ["toPhase"] = "peak", ["toEdge"] = "bottom", ["toAt"] = 0.0,
        });
        return diagram;
    }

    /// <summary>A behavior model: a sequence with a check and a retry under it, the retry still without a child.</summary>
    public static DislDiagram BehaviorDiagram()
    {
        var diagram = new DislDiagram(BehaviorModel);
        var root = diagram.AddNode("Sequence", "1", new Dictionary<string, object?> { ["label"] = "Answer" });
        diagram.AddNode("Check", "1.1", new Dictionary<string, object?> { ["label"] = "Is it known?" }, root);
        diagram.AddNode("Retry", "1.2", new Dictionary<string, object?> { ["label"] = "", ["attempts"] = 3L }, root);
        return diagram;
    }

    public static DislElement Element(DislDiagram diagram, string id) => diagram.ElementById(id) ?? throw new InvalidOperationException($"No element {id}.");
}
