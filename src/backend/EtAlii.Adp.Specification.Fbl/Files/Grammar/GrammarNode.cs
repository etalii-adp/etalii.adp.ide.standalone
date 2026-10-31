using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Files.Grammar;

/// <summary>
/// A node or token of the tree a grammar gives a body (FBL 0.9 §4.9, ATD §4.5): named by its rule, the alias of the
/// alternative that matched, or its terminal. A node's own span is its container span, the tokens the tree leaves out
/// included; a node that matched no token has none, and an optional part that matched nothing is an empty part.
/// </summary>
internal sealed class GrammarNode : Entry
{
    public bool IsToken { get; init; }

    /// <summary>Whether the node matched a token; when not, <see cref="Entry.Own"/> is an empty span where it stands.</summary>
    public bool HasSpan { get; init; }

    /// <summary>An optional part that matched nothing: a part of its parent, never one of its <see cref="Entry.Children"/>.</summary>
    public bool Empty { get; init; }

    /// <summary>The node's place in document order, its parent before it.</summary>
    public int Order { get; init; }

    /// <summary>The parts of the node in order, the empty ones included, as CEL's <c>children</c> sees them.</summary>
    public List<GrammarNode> Parts { get; } = [];
}
