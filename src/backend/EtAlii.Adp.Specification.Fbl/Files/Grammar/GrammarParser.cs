using System.Text;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Lark;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// What reading a body by its grammar gave: the tree, its nodes in document order, its tokens and its comments, and the
/// text a body read in part skipped, in bytes.
/// </summary>
internal sealed record GrammarReading(GrammarNode Root, IReadOnlyList<GrammarNode> Nodes, IReadOnlyList<Span> Tokens, IReadOnlyList<Span> Comments, IReadOnlyList<GrammarSkipped> Skipped)
{
    /// <summary>The byte offset of the <c>open</c> token of each block the body ends inside (FBL 0.11 §4.9, rule 6).</summary>
    public IReadOnlyList<int> Unclosed { get; init; } = [];

    /// <summary>The tokens of an ignored terminal of <c>recover.unclosed</c> that do not end with its text (FBL 0.11 §4.9, rule 8).</summary>
    public IReadOnlyList<Span> UnclosedComments { get; init; } = [];
}

/// <summary>
/// Text a body read in part skipped (FBL 0.9 §4.9), in bytes: all of it, and where reading stopped in it;
/// <paramref name="Unmatched"/> for a close token that closes no block (FBL 0.11 §4.9, rule 7).
/// </summary>
internal sealed record GrammarSkipped(Span Text, int Stop, bool Unmatched);

/// <summary>A body that does not read by its grammar (FBL §7.5): where, in bytes, and why.</summary>
internal sealed class GrammarSyntaxException(int offset, string message) : Exception(message)
{
    public int Offset { get; } = offset;
}

/// <summary>
/// Reads a body by its grammar (FBL 0.9 §4.9): the whole body is decoded once with a table from each UTF-16 position to
/// its byte offset (research R4), parsed by <c>EtAlii.Lark</c>, and its tree built over byte spans. Every token the lexer
/// cut is a leaf; an ignored one that holds a character other than whitespace is a comment (research R6).
/// </summary>
internal sealed class GrammarParser
{
    private readonly GrammarBuild _grammar;
    private readonly BodyText _text;
    private readonly string _source;
    private readonly int[] _bytes;
    private readonly List<GrammarNode> _nodes = [];

    public GrammarParser(GrammarBuild grammar, BodyText text)
    {
        _grammar = grammar;
        _text = text;
        _source = text.Text(text.BomLength, text.Length);
        _bytes = ByteOffsets(_source, text.BomLength);
    }

    /// <summary>
    /// The tree of the body: with <c>recover</c> read unit by unit, the root a node of the start rule whose children are the
    /// units read; without it parsed whole, throwing <see cref="GrammarSyntaxException"/> when it does not read (FBL §7.5).
    /// </summary>
    public GrammarReading Parse()
    {
        if (_grammar.Recover is not null) return ParseInPart();
        Tree tree;
        IReadOnlyDictionary<int, Token> tokens;
        try
        {
            (tree, tokens) = _grammar.Parse(_source, _grammar.Start);
        }
        catch (UnexpectedInputException e)
        {
            var at = e.PosInStream is >= 0 and var position && position <= _source.Length ? _bytes[position] : _text.Length;
            throw new GrammarSyntaxException(at, FirstLine(e.Message));
        }
        var root = Node(tree.Data, tree.Children, null, new Span(_text.BomLength, _text.Length));
        return Reading(root, tokens, []);
    }

    /// <summary>The body read unit by unit (FBL 0.9 §4.9, <i>Reading in part</i>): the units the root's children, the skipped text a leaf that binds nothing.</summary>
    private GrammarReading ParseInPart()
    {
        var read = _grammar.ParseUnits(_source);
        var root = Node(_grammar.Start, read.Units, null, new Span(_text.BomLength, _text.Length));
        var skipped = read.Skips.Select(skip => new GrammarSkipped(new Span(_bytes[skip.Start], _bytes[skip.End]), _bytes[skip.Stop], skip.Unmatched)).ToList();
        var unclosed = _grammar.Recover?.Unclosed ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var comments = read.Tokens.Values
            .Where(token => unclosed.TryGetValue(token.Type, out var closing) && !token.Value.EndsWith(closing, StringComparison.Ordinal))
            .OrderBy(token => token.StartPos)
            .Select(Span)
            .ToList();
        return Reading(root, read.Tokens, skipped) with { Unclosed = [.. read.Unclosed.Select(at => _bytes[at])], UnclosedComments = comments };
    }

    /// <summary>The reading of a tree: every token cut a leaf but whitespace, an ignored one a comment, and skipped text a leaf.</summary>
    private GrammarReading Reading(GrammarNode root, IReadOnlyDictionary<int, Token> tokens, IReadOnlyList<GrammarSkipped> skipped)
    {
        var leaves = new List<Span>();
        var comments = new List<Span>();
        foreach (var token in tokens.Values.OrderBy(t => t.StartPos))
        {
            var span = Span(token);
            var ignored = _grammar.Ignored.Contains(token.Type);
            if (ignored && token.Value.All(char.IsWhiteSpace)) continue;
            leaves.Add(span);
            if (ignored) comments.Add(span);
        }
        leaves.AddRange(skipped.Select(skip => skip.Text));
        return new GrammarReading(root, _nodes, leaves, comments, skipped);
    }

    /// <summary>A node of the tree and its parts, over its container span (ATD §4.5).</summary>
    private GrammarNode Node(Tree tree, GrammarNode? parent)
    {
        var meta = tree.Meta;
        var span = meta is { Empty: false, ContainerStartPos: { } from, ContainerEndPos: { } to } ? new Span(_bytes[from], _bytes[to]) : (Span?)null;
        return Node(tree.Data, tree.Children, parent, span);
    }

    /// <summary>A node named <paramref name="name"/> over <paramref name="span"/>, none when it read no token, and its parts.</summary>
    private GrammarNode Node(string name, IEnumerable<object?> children, GrammarNode? parent, Span? span)
    {
        var node = new GrammarNode
        {
            Name = name,
            Own = span ?? Here(parent),
            HasSpan = span is not null,
            Order = _nodes.Count,
            Parent = parent,
            Indent = Indent(span),
        };
        _nodes.Add(node);
        foreach (var child in children)
        {
            var part = child switch
            {
                Tree sub => Node(sub, node),
                Token token => Leaf(token, node),
                _ => new GrammarNode { Name = null, Own = Here(node), Empty = true, Parent = node, Order = -1 },
            };
            node.Parts.Add(part);
            if (!part.Empty) node.Children.Add(part);
        }
        return node;
    }

    private GrammarNode Leaf(Token token, GrammarNode parent)
    {
        var span = Span(token);
        var leaf = new GrammarNode { Name = token.Type, Own = span, IsToken = true, HasSpan = true, Order = _nodes.Count, Parent = parent, Indent = Indent(span) };
        _nodes.Add(leaf);
        return leaf;
    }

    /// <summary>Where a node with no span stands: at the end of what its parent read so far, else at its parent's start.</summary>
    private Span Here(GrammarNode? parent)
    {
        var at = parent is null ? _text.BomLength : parent.Children.Count > 0 ? parent.Children[^1].Own.End : parent.Own.Start;
        return new Span(at, at);
    }

    private Span Span(Token token) => new(_bytes[token.StartPos ?? 0], _bytes[token.EndPos ?? token.StartPos ?? 0]);

    private int Indent(Span? span) => span is { } s ? s.Start - _text.Lines[_text.LineIndexAt(s.Start)].Start : 0;

    /// <summary>The byte offset of every UTF-16 position of <paramref name="source"/>, and of its end, after a BOM of <paramref name="bom"/> bytes.</summary>
    private static int[] ByteOffsets(string source, int bom)
    {
        var offsets = new int[source.Length + 1];
        var at = bom;
        for (var i = 0; i < source.Length; i++)
        {
            offsets[i] = at;
            if (char.IsHighSurrogate(source[i]) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                offsets[++i] = at;
                at += 4;
                continue;
            }
            at += Encoding.UTF8.GetByteCount(source.AsSpan(i, 1));
        }
        offsets[source.Length] = at;
        return offsets;
    }

    private static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return (end < 0 ? message : message[..end]).Trim();
    }
}
