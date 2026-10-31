using System.Text;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Lark;

namespace EtAlii.Adp.Specification.Fbl.Files.Grammar;

/// <summary>What reading a body by its grammar gave: the tree, its nodes in document order, its tokens and its comments, in bytes.</summary>
internal sealed record GrammarReading(GrammarNode Root, IReadOnlyList<GrammarNode> Nodes, IReadOnlyList<Span> Tokens, IReadOnlyList<Span> Comments);

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

    /// <summary>The tree of the whole body; throws <see cref="GrammarSyntaxException"/> when it does not read.</summary>
    public GrammarReading Parse()
    {
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
        var root = Node(tree, null, new Span(_text.BomLength, _text.Length));
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
        return new GrammarReading(root, _nodes, leaves, comments);
    }

    /// <summary>A node of the tree and its parts; <paramref name="own"/> overrides its span, as the root's is the whole body.</summary>
    private GrammarNode Node(Tree tree, GrammarNode? parent, Span? own = null)
    {
        var meta = tree.Meta;
        var span = own ?? (meta is { Empty: false, ContainerStartPos: { } from, ContainerEndPos: { } to } ? new Span(_bytes[from], _bytes[to]) : null);
        var node = new GrammarNode
        {
            Name = tree.Data,
            Own = span ?? Here(parent),
            HasSpan = span is not null,
            Order = _nodes.Count,
            Parent = parent,
            Indent = Indent(span),
        };
        _nodes.Add(node);
        foreach (var child in tree.Children)
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
