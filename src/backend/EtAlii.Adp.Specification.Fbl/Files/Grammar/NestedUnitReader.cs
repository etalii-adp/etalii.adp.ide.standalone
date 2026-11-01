using EtAlii.Lark;
using EtAlii.Lark.Parsers;
using LarkParser = EtAlii.Lark.Lark;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// Reads one unit of a body in part inside its blocks too, by <c>recover.nested</c> (FBL 0.11 §4.9, <i>Reading in part</i>,
/// rules 4 to 6): for each block it is inside, the parser's state at the block's last boundary is kept, right after the
/// block's <c>open</c> token and after each <c>sync</c> token shifted at that depth. Where reading stops inside a block,
/// the text from that boundary is skipped to the end of the first <c>sync</c> token that ends after the stop, or to a
/// <c>close</c> token at the block's own depth, and reading continues from the boundary's state after it. A block still
/// open where the body ends is closed without text, a <c>sync</c> token first where the grammar needs one. Positions are
/// UTF-16 offsets of the text.
/// </summary>
internal sealed class NestedUnitReader(LarkParser reader, GrammarScanner scanner, string text, string unit, IReadOnlySet<string> sync, string open, string close)
{
    /// <summary>The text skipped inside blocks, by every unit read so far.</summary>
    public List<GrammarSkip> Skips { get; } = [];

    /// <summary>Where the <c>open</c> token of each block the body ends inside stands (rule 6).</summary>
    public List<int> Unclosed { get; } = [];

    /// <summary>
    /// One unit from <paramref name="position"/>: its tree and where it ends when the parser accepts it, else where it starts
    /// and where reading stopped outside every block; neither when only trivia is left.
    /// </summary>
    public UnitRead Read(int position)
    {
        var parser = reader.ParseInteractive(new TextSlice(text, position), unit);
        var blocks = new List<Boundary>();
        int? first = null;
        var end = position;
        var shifted = 0;
        var tokens = parser.LexerThread.Lex(parser.ParserState).GetEnumerator();
        while (true)
        {
            var before = ParserSnapshot.Of(parser);
            int stop;
            if (Next(tokens) is not { } next) break;
            if (next.Token is null)
            {
                stop = next.Stop ?? text.Length;
            }
            else if (Shift(parser, next.Token))
            {
                var token = next.Token!;
                first ??= token.StartPos;
                shifted++;
                end = token.EndPos ?? end;
                Mark(blocks, parser, token, end);
                continue;
            }
            else
            {
                stop = next.Token!.StartPos ?? text.Length;
                parser = before;
            }
            if (blocks.Count == 0)
            {
                if (shifted > 0 && Accepted(ParserSnapshot.Of(parser)) is { } tree) return new UnitRead(tree, first ?? position, null, end);
                return new UnitRead(null, Math.Min(first ?? stop, stop), stop, end);
            }
            var top = blocks[^1];
            var to = SkipEnd(stop, top.Position);
            Skips.Add(new GrammarSkip(top.Position, stop, to, false));
            blocks[^1] = top with { Position = to };
            parser = Resume(top.State, to);
            tokens = parser.LexerThread.Lex(parser.ParserState).GetEnumerator();
        }
        if (blocks.Count == 0)
        {
            if (shifted == 0) return new UnitRead(null, position, null, position);
            return Accepted(ParserSnapshot.Of(parser)) is { } whole ? new UnitRead(whole, first ?? position, null, end) : new UnitRead(null, first ?? position, text.Length, end);
        }
        parser = CloseAll(parser, blocks, end);
        return Accepted(parser) is { } closed ? new UnitRead(closed, first ?? position, null, end) : new UnitRead(null, first ?? position, text.Length, end);
    }

    /// <summary>The next token of <paramref name="tokens"/>, or where the lexer stopped at text no terminal the state can shift matches; neither at the end.</summary>
    private static Lexed? Next(IEnumerator<Token> tokens)
    {
        try
        {
            return tokens.MoveNext() ? new Lexed(tokens.Current, null) : null;
        }
        catch (UnexpectedInputException e)
        {
            return new Lexed(null, e.PosInStream);
        }
    }

    /// <summary>Whether the parser shifts <paramref name="token"/>; when it does not, it may have reduced first, so its state before is taken back.</summary>
    private static bool Shift(InteractiveParser parser, Token? token)
    {
        if (token is null) return false;
        try
        {
            parser.FeedToken(token);
            return true;
        }
        catch (UnexpectedInputException)
        {
            return false;
        }
    }

    /// <summary>A boundary after a shifted token (rule 4): a block opened, a block closed, or a <c>sync</c> token at the innermost block's depth.</summary>
    private void Mark(List<Boundary> blocks, InteractiveParser parser, Token token, int end)
    {
        if (token.Type == open) blocks.Add(new Boundary(ParserSnapshot.Of(parser.ParserState), end, token.StartPos ?? end));
        else if (token.Type == close && blocks.Count > 0) blocks.RemoveAt(blocks.Count - 1);
        else if (blocks.Count > 0 && sync.Contains(token.Type)) blocks[^1] = blocks[^1] with { State = ParserSnapshot.Of(parser.ParserState), Position = end };
    }

    /// <summary>
    /// Where text skipped inside a block ends (rule 5); a token that the boundary's state could not shift either, and
    /// that would end no skip, is skipped alone, so that reading always moves on.
    /// </summary>
    private int SkipEnd(int stop, int boundary)
    {
        var to = scanner.EndOfSkip(text, stop, sync, open, close) ?? text.Length;
        if (to > boundary) return to;
        return scanner.TokenAt(text, stop) is { } token ? stop + token.Length : Math.Min(stop + 1, text.Length);
    }

    /// <summary>A parser in the boundary's state, lexing from <paramref name="at"/>; the boundary is left as it is, as a later stop in the same block resumes from it again.</summary>
    private InteractiveParser Resume(ParserState state, int at)
    {
        var parser = reader.ParseInteractive(new TextSlice(text, at), unit);
        var boundary = ParserSnapshot.Of(state);
        parser.ParserState.StateStack.Clear();
        parser.ParserState.StateStack.AddRange(boundary.StateStack);
        parser.ParserState.ValueStack.Clear();
        parser.ParserState.ValueStack.AddRange(boundary.ValueStack);
        return parser;
    }

    /// <summary>
    /// Every block still open where the body ends closed without text, innermost first (rule 6), a <c>sync</c> token first
    /// where the grammar needs one; a block whose last statement cannot end there has the text from its boundary skipped.
    /// </summary>
    private InteractiveParser CloseAll(InteractiveParser parser, List<Boundary> blocks, int end)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var block = blocks[i];
            Unclosed.Add(block.Open);
            if (Close(parser, end)) continue;
            Skips.Add(new GrammarSkip(block.Position, text.Length, text.Length, false));
            parser = Resume(block.State, text.Length);
            Close(parser, end);
        }
        return parser;
    }

    /// <summary>Feeds the innermost block's <c>close</c>, without text at <paramref name="at"/>, a <c>sync</c> token before it when the parser needs one; false when it takes neither.</summary>
    private bool Close(InteractiveParser parser, int at)
    {
        var accepts = parser.Accepts();
        if (accepts.Contains(close)) return Shift(parser, Synthetic(close, at));
        if (sync.FirstOrDefault(accepts.Contains) is not { } needed) return false;
        var trial = ParserSnapshot.Of(parser);
        if (!Shift(trial, Synthetic(needed, at)) || !trial.Accepts().Contains(close)) return false;
        return Shift(parser, Synthetic(needed, at)) && Shift(parser, Synthetic(close, at));
    }

    private static Token Synthetic(string type, int at) => new(type, "", at, null, null, null, null, at);

    /// <summary>The unit's tree, when the parser accepts it where it is; null when it does not.</summary>
    private static object? Accepted(InteractiveParser parser)
    {
        try
        {
            return parser.FeedEof();
        }
        catch (UnexpectedInputException)
        {
            return null;
        }
    }

    /// <summary>A block the reader is inside: the parser's state at its last boundary, where that boundary is, and where its <c>open</c> token stands.</summary>
    private sealed record Boundary(ParserState State, int Position, int Open);

    /// <summary>A token lexed, or where lexing stopped.</summary>
    private sealed record Lexed(Token? Token, int? Stop);
}
