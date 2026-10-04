using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>The palette: the eleven kinds of node, each naming the add action its drop commits.</summary>
/// <remarks>The drop and the placement menu run the same action, so there is no second implementation to disagree with.</remarks>
public sealed class AbmToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        Item(AbmNodeKinds.Sequence, "Do in order", "mdi-arrow-right-bold-outline"),
        Item(AbmNodeKinds.Fallback, "Try in order", "mdi-help-rhombus-outline"),
        Item(AbmNodeKinds.Parallel, "Do together", "mdi-call-split"),
        Item(AbmNodeKinds.Retry, "Retry", "mdi-replay"),
        Item(AbmNodeKinds.Repeat, "Repeat until", "mdi-repeat"),
        Item(AbmNodeKinds.Guard, "Only while", "mdi-shield-outline"),
        Item(AbmNodeKinds.Approval, "Ask approval before", "mdi-account-check-outline"),
        Item(AbmNodeKinds.Check, "Check", "mdi-help-circle-outline"),
        Item(AbmNodeKinds.Action, "Do", "mdi-play-outline"),
        Item(AbmNodeKinds.Ask, "Ask the user", "mdi-account-question-outline"),
        Item(AbmNodeKinds.Delegate, "Delegate", "mdi-account-arrow-right-outline"),
    ];

    private static ToolboxItemDefinition Item(string kind, string label, string icon) =>
        new($"abm.toolbox.{kind}", label, icon, Describe(kind), AbmContextActionProvider.AddActionId(kind));

    private static string Describe(string kind)
    {
        var meaning = AbmNodeKinds.Of(kind).Meaning;
        return char.ToUpperInvariant(meaning[0]) + meaning[1..] + ". Drop it below the node it belongs under.";
    }
}
