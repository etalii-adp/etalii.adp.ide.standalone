using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>One node of the tree, as it was read from one list item and the lines under it.</summary>
/// <param name="Id">
/// Its place in the tree: <c>1</c> for the first root, <c>1.2</c> for that root's second child.
/// A place rather than a name, so a rename keeps the selection and the position it was dragged to.
/// </param>
/// <param name="Kind">One of <see cref="AbmNodeKinds"/>' ids.</param>
/// <param name="Label">The text after the keyword.</param>
/// <param name="RetryCount">A Retry's attempts; zero for every other kind.</param>
/// <param name="Notes">The plain lines under the item, without their indentation; empty when there are none.</param>
/// <param name="HasKeyword">
/// Whether the item carried a keyword. An item without one is read as a Do, so a hand-written list
/// still draws, and the validator says it was read that way.
/// </param>
/// <param name="Line">The item's own line.</param>
/// <param name="NotesRange">The notes' lines, or null when there are none.</param>
/// <param name="SubtreeEnd">The last line that belongs to this node: its notes, or its last descendant's.</param>
/// <param name="Indent">The column the item's marker sits at.</param>
/// <param name="ContentIndent">The column the item's text starts at - where its notes are written.</param>
/// <param name="Marker">The list marker: <c>-</c>, <c>*</c> or <c>+</c>.</param>
/// <param name="KeywordText">The keyword exactly as written, so a rename keeps the author's own spelling.</param>
/// <param name="ParentId">The parent's id; null for a root.</param>
/// <param name="ChildIds">The children's ids, in order.</param>
public sealed record AbmNode(
    string Id,
    string Kind,
    string Label,
    int RetryCount,
    string Notes,
    bool HasKeyword,
    int Line,
    LineRange? NotesRange,
    int SubtreeEnd,
    int Indent,
    int ContentIndent,
    char Marker,
    string KeywordText,
    string? ParentId,
    IReadOnlyList<string> ChildIds)
{
    /// <summary>What the node may hold beneath it.</summary>
    public AbmNodeCategory Category => AbmNodeKinds.Of(Kind).Category;

    /// <summary>Whether another child may be added beneath it.</summary>
    public bool TakesAnotherChild => Category switch
    {
        AbmNodeCategory.Composite => true,
        AbmNodeCategory.Decorator => ChildIds.Count == 0,
        _ => false,
    };

    /// <summary>Every line that belongs to this node, its own and its descendants'.</summary>
    public LineRange Subtree => new(Line, SubtreeEnd);

    /// <summary>The keyword as the canvas shows it: the canonical one, with a Retry's count.</summary>
    public string Keyword => AbmNodeKinds.KeywordOf(Kind, RetryCount);
}
