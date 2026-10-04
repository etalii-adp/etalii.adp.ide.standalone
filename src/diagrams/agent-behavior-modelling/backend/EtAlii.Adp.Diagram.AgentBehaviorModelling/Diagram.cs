using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>This diagram type's identity, cataloged in docs/tools.md as `etalii/agent-behavior-modelling`.</summary>
/// <remarks>
/// <para>
/// <b>The body is Markdown, and the Markdown is the agent's instructions.</b> A behavior model is
/// a behavior tree written as a nested list under a <c>Behavior</c> heading, in a file a chat
/// agent reads as it is: a system prompt, an <c>AGENTS.md</c>, a skill. The <c>.adp</c> beside it
/// holds only the visualization - the positions an author dragged nodes to - so the agent never
/// reads a coordinate and the diagram never invents logic.
/// </para>
/// <para>
/// <b>Declared shared</b>, as <c>.yml</c> is for pipelines: a repository is full of Markdown that
/// is not a behavior model, so this type never claims an <c>.md</c> on sight. A file becomes one
/// when the user registers it through Add, and <see cref="SuggestsBody"/> narrows the offer to the
/// files that already carry a Behavior heading.
/// </para>
/// </remarks>
public static partial class Diagram
{
    /// <summary>The body of a behavior model is a Markdown file.</summary>
    public const string DocumentExtension = ".md";

    /// <summary>The agent behavior model.</summary>
    public static DiagramDefinition AgentBehaviorModelling { get; } = new(
        new DiagramOrigin("etalii", "agent-behavior-modelling"),
        "Agent Behavior Modelling",
        "A chat agent's instructions drawn as a behavior tree - what it checks, does, tries, repeats and asks - stored as the Markdown the agent reads.",
        Icon: "mdi-robot-outline",
        Extension: DocumentExtension,
        SharedExtension: true,
        // A notation this repository defines, so ADP owns both the format and the etalii/ origin.
        // Positions are computed from the tree; an author's drag is kept in the .adp, never in the
        // Markdown the agent reads.
        Build: builder => builder.Services.AddAgentBehaviorModelling(),
        SuggestsBody: SuggestsBody);

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [AgentBehaviorModelling];

    /// <summary>Whether a Markdown file already reads as a behavior model: it has a Behavior heading.</summary>
    public static bool SuggestsBody(string text) =>
        text is { Length: > 0 } && BehaviorHeading().IsMatch(text);

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+behaviou?r\s*#*\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BehaviorHeading();
}
