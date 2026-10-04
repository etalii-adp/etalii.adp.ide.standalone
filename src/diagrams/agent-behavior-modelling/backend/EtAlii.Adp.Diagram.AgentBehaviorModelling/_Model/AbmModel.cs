namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>What <see cref="AbmParser"/> read from one Markdown file.</summary>
/// <param name="Nodes">Every node, in document order - which is also depth-first order.</param>
/// <param name="SectionLine">The Behavior heading's line; null when the file has none.</param>
/// <param name="SectionEnd">The last line of the Behavior section; meaningless without a heading.</param>
/// <param name="ListIndent">The column the tree's root items sit at.</param>
/// <param name="Problems">What the parser passed over, with the line it was on.</param>
public sealed record AbmModel(
    IReadOnlyList<AbmNode> Nodes,
    int? SectionLine,
    int SectionEnd,
    int ListIndent,
    IReadOnlyList<AbmProblem> Problems)
{
    private readonly Dictionary<string, AbmNode> _byId = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

    /// <summary>A model with nothing in it: the file has no Behavior section.</summary>
    public static AbmModel Empty { get; } = new([], null, 0, 0, []);

    /// <summary>The nodes with no parent, in order.</summary>
    public IReadOnlyList<AbmNode> Roots => [.. Nodes.Where(node => node.ParentId is null)];

    /// <summary>The node with <paramref name="id"/>, or null.</summary>
    public AbmNode? NodeOf(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The children of <paramref name="node"/>, in order.</summary>
    public IReadOnlyList<AbmNode> ChildrenOf(AbmNode node) => [.. node.ChildIds.Select(id => _byId[id])];

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="node"/> or lies beneath it.</summary>
    public static bool IsWithin(string candidate, string node) =>
        candidate == node || candidate.StartsWith(node + ".", StringComparison.Ordinal);
}

/// <summary>Something the parser or the rules have to say about a document, on a line.</summary>
/// <param name="RuleId">A stable id: <c>abm.no-keyword</c> and its kin.</param>
/// <param name="Message">A sentence a person can act on.</param>
/// <param name="Line">The zero-based line it is about.</param>
public sealed record AbmProblem(string RuleId, string Message, int Line);
