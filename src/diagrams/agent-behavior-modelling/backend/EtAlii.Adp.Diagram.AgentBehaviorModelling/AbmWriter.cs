using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// Splices the edits a behavior model supports into its Markdown: the lines a change touches, and
/// none of the others.
/// </summary>
/// <remarks>
/// <para>
/// <b>A node is one line, its notes the lines under it, its subtree a contiguous range.</b> That
/// is what makes every edit here a splice: a rename rewrites one line, notes replace their own
/// range, a removal cuts the subtree's range and a move cuts it and inserts it elsewhere,
/// re-indented by the difference between the two places.
/// </para>
/// <para>
/// <b>New lines follow the file, not a house style.</b> A new child takes its first sibling's
/// indentation and list marker when it has siblings, and the parent's text column when it has
/// none - which is where Markdown expects a nested item to start.
/// </para>
/// </remarks>
public static class AbmWriter
{
    /// <summary>
    /// The text of a new behavior model for an agent called <paramref name="name"/>, with the
    /// ending the caller asks for: a Markdown file an agent can follow from the first save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It explains itself to the agent.</b> A model reading the file has no diagram beside it, so the
    /// file says what a behavior tree is and what each keyword means, in a section of its own before
    /// the tree. That section is ordinary prose to this module - never parsed, never rewritten - and an
    /// author who would rather keep the instructions short may delete it.
    /// </para>
    /// <para>
    /// <b>It also starts the tree</b>, with one Do in order under the Behavior heading, so a new diagram
    /// opens with something to add children to rather than an empty canvas. The registration writes
    /// it with CRLF, because a file ADP creates has no style of its own to preserve, and CRLF is the
    /// repository's.
    /// </para>
    /// </remarks>
    public static string EmptyDocument(string name, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        var title = OneLine(name);
        return string.Join(lineEnding, [
            $"# {(title.Length > 0 ? title : "Agent")}",
            "",
            "Describe the agent here: who it works for, and what it is for.",
            "",
            .. Legend(),
            "",
            "## Behavior",
            "",
            ItemLine("- ", AbmNodeKinds.KeywordOf(AbmNodeKinds.Sequence, 0), "Handle the request"),
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

    /// <summary>Sets a node's label, keeping its keyword as the author wrote it.</summary>
    public static EditOutcome SetLabel(LineDocument document, AbmNode node, string label)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        var text = OneLine(label);
        var line = node.HasKeyword
            ? ItemLine(PrefixOf(document, node), node.KeywordText, text)
            : PrefixOf(document, node) + text;
        document.Replace(new LineRange(node.Line, node.Line), [line.TrimEnd()]);
        return EditOutcome.Applied;
    }

    /// <summary>Changes a node's kind, refusing a kind that cannot hold the children it already has.</summary>
    public static EditOutcome SetKind(LineDocument document, AbmNode node, string kind, int retryCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        if (!AbmNodeKinds.IsKnown(kind))
        {
            return EditOutcome.Refused($"A behavior model has no `{kind}` node.");
        }

        if (kind == AbmNodeKinds.Retry && retryCount < 1)
        {
            return EditOutcome.Refused("A Retry allows at least one attempt.");
        }

        var category = AbmNodeKinds.Of(kind).Category;
        if (category == AbmNodeCategory.Leaf && node.ChildIds.Count > 0)
        {
            return EditOutcome.Refused($"\"{AbmNodeKinds.Of(kind).Keyword}\" holds no children, and this node has {Count(node.ChildIds.Count, "child", "children")}.");
        }

        if (category == AbmNodeCategory.Decorator && node.ChildIds.Count > 1)
        {
            return EditOutcome.Refused($"\"{AbmNodeKinds.KeywordOf(kind, retryCount)}\" holds exactly one child, and this node has {node.ChildIds.Count}.");
        }

        var line = ItemLine(PrefixOf(document, node), AbmNodeKinds.KeywordOf(kind, kind == AbmNodeKinds.Retry ? retryCount : 0), node.Label);
        document.Replace(new LineRange(node.Line, node.Line), [line.TrimEnd()]);
        return EditOutcome.Applied;
    }

    /// <summary>Replaces a node's notes; empty notes remove them.</summary>
    public static EditOutcome SetNotes(LineDocument document, AbmNode node, string notes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        var indent = new string(' ', node.ContentIndent);
        var lines = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n').Split('\n')
            .Select(line => line.Trim().Length == 0 ? "" : indent + line.TrimEnd())
            .ToList();
        var empty = lines.All(line => line.Length == 0);

        switch (node.NotesRange)
        {
            case { } range when empty:
                document.Remove(range);
                break;
            case { } range:
                document.Replace(range, lines);
                break;
            case null when !empty:
                document.Insert(node.Line + 1, lines);
                break;
        }

        return EditOutcome.Applied;
    }

    /// <summary>
    /// Adds a node under <paramref name="parent"/> - or as a root when it is null - before the child
    /// now at <paramref name="index"/>, or after the last when the index is past them.
    /// </summary>
    /// <returns>The edit, and the new node's id when it was applied.</returns>
    public static (EditOutcome Edit, string NodeId) Add(
        LineDocument document, AbmModel model, AbmNode? parent, int index, string kind, string label, int retryCount = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        if (!AbmNodeKinds.IsKnown(kind))
        {
            return (EditOutcome.Refused($"A behavior model has no `{kind}` node."), "");
        }

        if (kind == AbmNodeKinds.Retry && retryCount < 1)
        {
            retryCount = AbmNodeKinds.DefaultRetryCount;
        }

        var keyword = AbmNodeKinds.KeywordOf(kind, retryCount);
        var text = OneLine(label);

        if (parent is null)
        {
            var roots = model.Roots;
            if (roots.Count == 0)
            {
                AddFirstRoot(document, model, ItemLine("- ", keyword, text).TrimEnd());
                return (EditOutcome.Applied, "1");
            }

            var at = Math.Clamp(index < 0 ? roots.Count : index, 0, roots.Count);
            var line = at < roots.Count ? roots[at].Line : roots[^1].SubtreeEnd + 1;
            document.Insert(line, [ItemLine(PrefixFor(document, roots[0]), keyword, text).TrimEnd()]);
            return (EditOutcome.Applied, (at + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!parent.TakesAnotherChild)
        {
            return (EditOutcome.Refused(parent.Category == AbmNodeCategory.Leaf
                ? $"\"{parent.Keyword}\" holds no children. Add the node under a Do in order, Try in order or Do together."
                : $"\"{parent.Keyword}\" holds exactly one child, and already has it."), "");
        }

        var children = model.ChildrenOf(parent);
        var position = Math.Clamp(index < 0 ? children.Count : index, 0, children.Count);
        var insertAt = position < children.Count ? children[position].Line : parent.SubtreeEnd + 1;
        var prefix = children.Count > 0 ? PrefixFor(document, children[0]) : new string(' ', parent.ContentIndent) + parent.Marker + " ";
        document.Insert(insertAt, [ItemLine(prefix, keyword, text).TrimEnd()]);
        return (EditOutcome.Applied, $"{parent.Id}.{position + 1}");
    }

    /// <summary>Removes a node, its notes and everything beneath it.</summary>
    public static EditOutcome Remove(LineDocument document, AbmNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        document.Remove(node.Subtree);
        return EditOutcome.Applied;
    }

    /// <summary>
    /// Moves a node and its subtree under <paramref name="newParent"/> - or to the roots when it is
    /// null - before the child now at <paramref name="index"/> there, or after the last.
    /// </summary>
    /// <remarks>
    /// The index counts the target's children as they are BEFORE the move, the node itself
    /// included when it is one of them: moving the second of three children to index 0 makes it
    /// the first, to index 3 the last. So "one earlier" is the node's index minus one, and "one
    /// later" its index plus two.
    /// </remarks>
    public static EditOutcome Move(LineDocument document, AbmModel model, AbmNode node, AbmNode? newParent, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        if (newParent is not null)
        {
            if (AbmModel.IsWithin(newParent.Id, node.Id))
            {
                return EditOutcome.Refused("A node cannot move beneath itself.");
            }

            var accepts = newParent.Category switch
            {
                AbmNodeCategory.Composite => true,
                AbmNodeCategory.Decorator => newParent.ChildIds.Count == 0 || node.ParentId == newParent.Id,
                _ => false,
            };
            if (!accepts)
            {
                return EditOutcome.Refused(newParent.Category == AbmNodeCategory.Leaf
                    ? $"\"{newParent.Keyword}\" holds no children."
                    : $"\"{newParent.Keyword}\" holds exactly one child, and already has it.");
            }
        }

        var siblings = newParent is null ? model.Roots : model.ChildrenOf(newParent);
        var at = Math.Clamp(index < 0 ? siblings.Count : index, 0, siblings.Count);
        var current = node.ParentId == newParent?.Id ? siblings.ToList().FindIndex(sibling => sibling.Id == node.Id) : -1;
        if (current >= 0 && (at == current || at == current + 1))
        {
            return EditOutcome.Refused("That node is already there.");
        }

        var target = at < siblings.Count
            ? siblings[at].Line
            : (newParent?.SubtreeEnd ?? model.Roots[^1].SubtreeEnd) + 1;

        // The indentation it lands at: a sibling's other than itself, or the parent's text column.
        var neighbour = siblings.FirstOrDefault(sibling => sibling.Id != node.Id);
        var prefix = neighbour is not null
            ? PrefixFor(document, neighbour)
            : newParent is not null
                ? new string(' ', newParent.ContentIndent) + newParent.Marker + " "
                : new string(' ', model.ListIndent) + node.Marker + " ";
        var newIndent = AbmParser.ColumnOf(prefix);
        var delta = newIndent - node.Indent;

        var moved = new List<string>();
        for (var line = node.Line; line <= node.SubtreeEnd; line++)
        {
            var text = document.Lines[line].Text;
            if (line == node.Line)
            {
                moved.Add(prefix + text[PrefixOf(document, node).Length..]);
            }
            else if (text.Trim().Length == 0)
            {
                moved.Add("");
            }
            else
            {
                moved.Add(delta >= 0 ? new string(' ', delta) + text : Outdent(text, -delta));
            }
        }

        if (target > node.SubtreeEnd)
        {
            document.Insert(target, moved);
            document.Remove(node.Subtree);
        }
        else
        {
            document.Remove(node.Subtree);
            document.Insert(target, moved);
        }

        return EditOutcome.Applied;
    }

    /// <summary>One item line: the prefix, then the keyword in bold with its colon, then the label.</summary>
    private static string ItemLine(string prefix, string keyword, string label) =>
        label.Length > 0 ? $"{prefix}**{keyword}:** {label}" : $"{prefix}**{keyword}:**";

    /// <summary>A label as one line: newlines become spaces, and the ends are trimmed.</summary>
    private static string OneLine(string label) =>
        string.Join(' ', label
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0));

    /// <summary>The first root of a file that has none: under its Behavior heading, or in a new Behavior section at the end.</summary>
    private static void AddFirstRoot(LineDocument document, AbmModel model, string item)
    {
        if (model.SectionLine is { } heading)
        {
            document.Insert(heading + 1, ["", item]);
            return;
        }

        var lines = document.Lines;
        var opening = lines.Count > 0 && lines[^1].Text.Trim().Length > 0 ? new[] { "" } : [];
        document.Insert(lines.Count, [.. opening, "## Behavior", "", item]);
    }

    /// <summary>The item line's indentation and marker, as written: everything before its text.</summary>
    private static string PrefixOf(LineDocument document, AbmNode node)
    {
        var text = document.Lines[node.Line].Text;
        var index = 0;
        while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
        {
            index++;
        }

        index++; // the marker
        while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
        {
            index++;
        }

        return text[..Math.Min(index, text.Length)];
    }

    /// <summary>A sibling's prefix, normalised to one space after the marker.</summary>
    private static string PrefixFor(LineDocument document, AbmNode sibling) =>
        PrefixOf(document, sibling).TrimEnd() + " ";

    private static string Outdent(string text, int columns)
    {
        var column = 0;
        var index = 0;
        while (index < text.Length && column < columns && (text[index] == ' ' || text[index] == '\t'))
        {
            column += text[index] == '\t' ? 4 - (column % 4) : 1;
            index++;
        }

        return text[index..];
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}
