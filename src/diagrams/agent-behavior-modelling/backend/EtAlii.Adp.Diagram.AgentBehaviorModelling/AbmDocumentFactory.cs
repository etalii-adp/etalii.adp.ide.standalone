using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>The text of a new behavior model: a Markdown file an agent can follow from the first save.</summary>
/// <remarks>
/// <para>
/// <b>It explains itself to the agent.</b> A model reading the file has no diagram beside it, so the
/// file says what a behavior tree is and what each keyword means, in a section of its own before
/// the tree. That section is ordinary prose to this module - never parsed, never rewritten - and an
/// author who would rather keep the instructions short may delete it.
/// </para>
/// <para>
/// <b>It also starts the tree</b>, with one Do in order under the Behavior heading, so a new diagram
/// opens with something to add children to rather than an empty canvas.
/// </para>
/// <para>
/// <b>It is the module's <see cref="IDiagramDocumentFactory"/>, and the host will not start
/// without one</b>, as for every type that declares an extension.
/// </para>
/// </remarks>
public sealed class AbmDocumentFactory : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    /// <remarks>CRLF, because a file ADP creates has no style of its own to preserve, and CRLF is the repository's.</remarks>
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return EmptyDocument(baseName, "\r\n");
    }

    /// <summary>The document text for an agent called <paramref name="name"/>, with the ending the caller asks for.</summary>
    public static string EmptyDocument(string name, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        var title = AbmWriter.OneLine(name);
        return string.Join(lineEnding, [
            $"# {(title.Length > 0 ? title : "Agent")}",
            "",
            "Describe the agent here: who it works for, and what it is for.",
            "",
            .. Legend(),
            "",
            "## Behavior",
            "",
            AbmWriter.ItemLine("- ", AbmNodeKinds.KeywordOf(AbmNodeKinds.Sequence, 0), "Handle the request"),
        ]) + lineEnding;
    }

    /// <summary>The section that tells the agent how to follow the tree.</summary>
    public static IReadOnlyList<string> Legend() =>
    [
        "## How to follow the behavior",
        "",
        "The behavior below is a behavior tree, and it is your instructions. Start at its first node and work through it from the top. Each list item is a node; the items indented under a node are its children, in order. Every node ends in success or failure, and its parent decides what happens next:",
        "",
        .. AbmNodeKinds.All.Select(kind => $"- **{kind.Keyword}** {kind.Meaning}."),
        "",
        "Lines under a node that are not list items are notes: follow them while you carry out that node.",
    ];
}
