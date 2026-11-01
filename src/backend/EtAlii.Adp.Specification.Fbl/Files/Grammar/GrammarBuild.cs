using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using EtAlii.Lark;
using EtAlii.Lark.Parsers;
using LarkParser = EtAlii.Lark.Lark;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// The grammar of a binding of the <c>grammar</c> family (FBL 0.9 §4.9), built once when the binding loads: checked as
/// ATD grammar notation, bounded (FBL §16), and built by <c>EtAlii.Lark</c> as a strict LALR(1) parser with the
/// contextual lexer of ATD §3.6, so that a grammar with a conflict, or with two terminals one state accepts that match
/// the same text, refuses its binding. Every Lark and LALR(1) use goes through <c>EtAlii.Lark</c> (Peter, 2026-10-23).
/// </summary>
internal sealed class GrammarBuild
{
    /// <summary>The most bytes of grammar text, imports included, that a binding may carry (FBL §16).</summary>
    public const int MaxTextBytes = 256 * 1024;

    /// <summary>The most rules a grammar may build into, after ATD §4.4's expansion (FBL §16).</summary>
    public const int MaxRules = 4096;

    /// <summary>The most states the grammar's automaton may have (FBL §16).</summary>
    public const int MaxStates = 32768;

    private GrammarBuild(LarkParser lark, string start, GrammarRecover? recover)
    {
        Lark = lark;
        Start = start;
        Recover = recover;
        Terminals = lark.Terminals.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Ignored = lark.IgnoreTokens.ToHashSet(StringComparer.Ordinal);
        StringTerminals = lark.Terminals.Where(t => t.Pattern is PatternStr).GroupBy(t => t.Pattern.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        Names = lark.Rules.Select(r => r.Origin.Name).Concat(lark.Rules.Select(r => r.Alias).OfType<string>()).Concat(Terminals).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The parser, with the start rule and, with <c>recover</c>, its unit as start symbols.</summary>
    private LarkParser Lark { get; }

    public string Start { get; }

    /// <summary><c>body.recover</c>: with it a body is read unit by unit (FBL 0.9 §4.9, <i>Reading in part</i>).</summary>
    public GrammarRecover? Recover { get; }

    /// <summary>The names of the grammar's terminals, the anonymous ones under the names Lark gives them.</summary>
    private IReadOnlySet<string> Terminals { get; }

    /// <summary>The terminals the grammar ignores (<c>%ignore</c>): its trivia.</summary>
    public IReadOnlySet<string> Ignored { get; }

    /// <summary>The terminal of each string the grammar matches as written, by that text (<c>"."</c> is <c>DOT</c>).</summary>
    private IReadOnlyDictionary<string, string> StringTerminals { get; }

    /// <summary>Every name a node or token of the tree can have: the rules, the aliases and the terminals.</summary>
    public IReadOnlySet<string> Names { get; }

    /// <summary>The terminal a <c>sync</c> entry names: a terminal's name, or the text of an anonymous string terminal; null for neither.</summary>
    public string? Terminal(string entry) => Terminals.Contains(entry) ? entry : StringTerminals.GetValueOrDefault(entry);

    private readonly Lock _gate = new();
    private LarkParser? _reader;
    private GrammarScanner? _scanner;
    private Dictionary<int, Token> _cut = [];

    /// <summary>
    /// Parses <paramref name="text"/> from <paramref name="start"/>, and gives every token the lexer cut on the way, by its
    /// UTF-16 offset, the ignored ones and those the tree leaves out included (research R6). A token cut again at the same
    /// offset, as a lookahead is, is kept once. One parse at a time runs on a grammar.
    /// </summary>
    public (Tree Tree, IReadOnlyDictionary<int, Token> Tokens) Parse(string text, string start)
    {
        lock (_gate)
        {
            _reader ??= Reader();
            _cut = [];
            var tree = _reader.Parse(text, start);
            return (tree, _cut);
        }
    }

    /// <summary>
    /// Reads <paramref name="text"/> unit by unit, by <c>recover</c> (FBL 0.9 §4.9, <i>Reading in part</i>; research R9):
    /// from the start, and after each unit or skipped text, one unit is parsed with <c>recover.unit</c> as the start symbol.
    /// Where the parser can neither shift nor accept, reading stopped, and the text from the unit's first token to the end of
    /// the first <c>sync</c> token lexed from there is skipped, or to the end of the body when none follows. The tokens cut
    /// inside skipped text are not given. Positions are UTF-16 offsets of <paramref name="text"/>.
    /// </summary>
    public GrammarUnits ParseUnits(string text)
    {
        var recover = Recover ?? throw new InvalidOperationException("The grammar has no recover to read a body in part by.");
        lock (_gate)
        {
            _reader ??= Reader();
            _scanner ??= new GrammarScanner(Lark.Terminals, Lark.LexerConf.RegexOptions);
            _cut = [];
            var sync = recover.Sync.Select(Terminal).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var units = new List<object?>();
            var skips = new List<GrammarSkip>();
            var position = 0;
            while (position < text.Length)
            {
                var unit = ReadUnit(_reader, text, position, recover.Unit);
                if (unit.Tree is { } tree)
                {
                    units.Add(tree);
                    position = unit.End;
                    continue;
                }
                if (unit.Stop is not { } stop) break;
                var end = _scanner.EndOfSync(text, stop, sync) ?? text.Length;
                skips.Add(new GrammarSkip(unit.Start, stop, end));
                position = end;
            }
            foreach (var skip in skips)
            {
                foreach (var at in _cut.Keys.Where(at => at >= skip.Start && at < skip.End).ToList()) _cut.Remove(at);
            }
            return new GrammarUnits(units, skips, _cut);
        }
    }

    /// <summary>
    /// One unit from <paramref name="position"/>: its tree and where it ends when the parser accepts it after its last
    /// token, else where it starts and where reading stopped; neither when only trivia is left. The state before each
    /// token is kept, since a token the parser cannot shift may first make it reduce.
    /// </summary>
    private static UnitRead ReadUnit(LarkParser reader, string text, int position, string unit)
    {
        var parser = reader.ParseInteractive(new TextSlice(text, position), unit);
        var before = parser.Copy(false);
        Token? last = null;
        int? first = null;
        int? stop = null;
        var shifted = 0;
        var end = position;
        try
        {
            foreach (var token in parser.IterParse())
            {
                if (last is not null)
                {
                    shifted++;
                    end = last.EndPos ?? end;
                }
                before = parser.Copy(false);
                first ??= token.StartPos;
                last = token;
            }
            if (last is not null) (shifted, end, before) = (shifted + 1, last.EndPos ?? end, parser);
        }
        catch (UnexpectedInputException e)
        {
            stop = e.PosInStream ?? text.Length;
            // The lexer failing after the last token means the parser shifted it; the parser failing on it means it did not.
            if (last is { StartPos: { } from } && stop > from) (shifted, end, before) = (shifted + 1, last.EndPos ?? end, parser);
        }
        if (shifted > 0 && Accepted(before) is { } tree) return new UnitRead(tree, first ?? position, null, end);
        if (last is null && stop is null) return new UnitRead(null, position, null, position);
        var at = stop ?? text.Length;
        return new UnitRead(null, Math.Min(first ?? at, at), at, end);
    }

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

    /// <summary>What reading one unit gave: its tree, or where reading stopped; <paramref name="End"/> is the end of its last token shifted.</summary>
    private sealed record UnitRead(object? Tree, int Start, int? Stop, int End);

    /// <summary>
    /// The parser reading bodies: the built one, saved and loaded again with a lexer callback on each of its terminals,
    /// since <c>EtAlii.Lark</c> takes lexer callbacks only when a parser is made and the terminals' names are known only
    /// once it is built. Loading needs no second build.
    /// </summary>
    private LarkParser Reader()
    {
        using var saved = new MemoryStream();
        Lark.Save(saved);
        saved.Position = 0;
        var callbacks = Terminals.ToDictionary(name => name, _ => (Func<Token, Token>)Record, StringComparer.Ordinal);
        return LarkParser.Load(saved, new LarkOptions { LexerCallbacks = callbacks, PropagatePositions = true });
    }

    private Token Record(Token token)
    {
        if (token.StartPos is { } at) _cut[at] = token;
        return token;
    }

    /// <summary>
    /// Builds <paramref name="body"/>'s grammar, its file and imports read from <paramref name="folder"/>, the FBL
    /// document's own folder; null when it is refused, each refusal a problem at <paramref name="pointer"/>, the body's.
    /// </summary>
    public static GrammarBuild? Build(GrammarBody body, string? folder, string pointer, List<LoadDiagnostic> problems)
    {
        var at = pointer + "/grammar";
        var text = body.Text;
        var source = "the binding's grammar";
        if (body.File is { } file)
        {
            if (!IsBesideFile(file) || folder is null || !File.Exists(Path.Combine(folder, file)))
            {
                problems.Add(Error(at + "/file", $"The grammar file '{file}' is not a file beside the FBL document (FBL 0.9 4.9)."));
                return null;
            }
            text = ReadText(Path.Combine(folder, file));
            source = file;
        }
        if (text is null) return null;

        var siblings = Siblings(folder);
        var texts = new List<(string Source, string Text)> { (source, text) };
        if (!ReadImports(text, folder, siblings, texts, at, problems)) return null;
        var size = texts.Sum(t => System.Text.Encoding.UTF8.GetByteCount(t.Text));
        if (size > MaxTextBytes)
        {
            problems.Add(Error(at, $"The grammar is {size} bytes with its imports, more than the {MaxTextBytes} a binding may carry (FBL 16)."));
            return null;
        }
        var refused = false;
        foreach ((string name, string content) in texts)
        {
            foreach (var problem in GrammarNotation.Problems(content, siblings))
            {
                problems.Add(Error(at, $"{name}, {problem}"));
                refused = true;
            }
        }
        if (refused) return null;
        if (body.Recover is { } recover && !texts.Any(t => GrammarNotation.RuleNames(t.Text).Contains(recover.Unit)))
        {
            // The unit is a start symbol of the build, so it is checked first: Lark would refuse it as an unknown start.
            problems.Add(Error(pointer + "/recover/unit", UnitIsNoRule(recover.Unit)));
            return null;
        }

        LarkParser lark;
        try
        {
            lark = new LarkParser(text, Options(body, folder));
        }
        catch (Exception e) when (e is LarkException or FileNotFoundException)
        {
            problems.Add(Error(at, $"The grammar does not build as an LALR(1) automaton from '{body.Start}': {OneLine(e.Message)} (FBL 0.9 4.9, 14.1)."));
            return null;
        }
        if (lark.Rules.Count > MaxRules)
        {
            problems.Add(Error(at, $"The grammar builds into {lark.Rules.Count} rules, more than the {MaxRules} a binding's grammar may have (FBL 16)."));
            return null;
        }
        var states = lark.ParseInteractive("", body.Start).ParserState.ParseConf.ParseTable.States.Count;
        if (states > MaxStates)
        {
            problems.Add(Error(at, $"The grammar's automaton has {states} states, more than the {MaxStates} a binding's grammar may have (FBL 16)."));
            return null;
        }
        var build = new GrammarBuild(lark, body.Start, body.Recover);
        return build.CheckRecover(pointer + "/recover", problems) ? build : null;
    }

    /// <summary>
    /// <c>recover</c> fits the grammar (FBL 0.9 §4.9): its unit is a rule, the start rule's one expansion is
    /// <c>unit*</c>, and every <c>sync</c> entry names a terminal or is the text of an anonymous string terminal.
    /// </summary>
    private bool CheckRecover(string pointer, List<LoadDiagnostic> problems)
    {
        if (Recover is not { } recover) return true;
        var ok = true;
        if (Lark.Rules.All(r => r.Origin.Name != recover.Unit))
        {
            problems.Add(Error(pointer + "/unit", UnitIsNoRule(recover.Unit)));
            ok = false;
        }
        else if (!StartIsUnitStar(recover.Unit))
        {
            problems.Add(Error(pointer + "/unit", $"The start rule '{Start}' is not '{recover.Unit}*', which recover asks for (FBL 0.9 4.9)."));
            ok = false;
        }
        for (var i = 0; i < recover.Sync.Count; i++)
        {
            if (Terminal(recover.Sync[i]) is not null) continue;
            problems.Add(Error($"{pointer}/sync/{i}", $"recover.sync names '{recover.Sync[i]}', no terminal of the grammar (FBL 0.9 4.9)."));
            ok = false;
        }
        return ok;
    }

    /// <summary>
    /// Whether the start rule is <c>unit*</c>: Lark compiles it to the empty expansion and one that is a repetition
    /// rule alone, whose expansions are <c>unit</c> and the repetition followed by <c>unit</c> (ATD §4.4).
    /// </summary>
    private bool StartIsUnitStar(string unit)
    {
        var shapes = Lark.Rules.Where(r => r.Origin.Name == Start).Select(r => r.Expansion.Select(s => s.Name).ToList()).ToList();
        var repetition = shapes.Where(s => s.Count > 0).ToList();
        return shapes.Count == 2 && shapes.Any(s => s.Count == 0) && repetition is [[var helper]]
            && Lark.Rules.Any(r => r.Origin.Name == helper && r.Expansion is [{ } only] && only.Name == unit);
    }

    private static LarkOptions Options(GrammarBody body, string? folder)
    {
        var start = new List<string> { body.Start };
        if (body.Recover is { } recover && recover.Unit != body.Start) start.Add(recover.Unit);
        return new LarkOptions
        {
            Parser = ParserKind.Lalr,
            Lexer = LexerKind.Contextual,
            Strict = true,
            MaybePlaceholders = true,
            PropagatePositions = true,
            KeepAllTokens = false,
            Start = start,
            RegexOptions = System.Text.RegularExpressions.RegexOptions.ECMAScript | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            ImportPaths = [new BesideLoader(folder)],
            SourcePath = folder is null ? null : Path.Combine(folder, "binding.lark"),
        };
    }

    /// <summary>Reads every grammar file the text imports, and theirs, in the order first imported; false when one is not beside the document.</summary>
    private static bool ReadImports(string text, string? folder, IReadOnlyCollection<string> siblings, List<(string Source, string Text)> texts, string at, List<LoadDiagnostic> problems)
    {
        var pending = new Queue<string>(GrammarNotation.Imports(text));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var name))
        {
            if (!seen.Add(name)) continue;
            if (folder is null || !siblings.Contains(name))
            {
                problems.Add(Error(at, $"The grammar imports '{name}', which is no grammar file beside the FBL document (FBL 0.9 4.9)."));
                return false;
            }
            var imported = ReadText(Path.Combine(folder, name + ".lark"));
            texts.Add((name + ".lark", imported));
            foreach (var next in GrammarNotation.Imports(imported)) pending.Enqueue(next);
        }
        return true;
    }

    /// <summary>The grammar files beside the document, by name without <c>.lark</c>.</summary>
    private static HashSet<string> Siblings(string? folder) =>
        folder is null || !Directory.Exists(folder)
            ? []
            : Directory.GetFiles(folder, "*.lark").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a <c>file</c> names a file directly beside the document: a name ending in <c>.lark</c>, with no folder.</summary>
    private static bool IsBesideFile(string file) =>
        file.EndsWith(".lark", StringComparison.Ordinal) && file.IndexOfAny(['/', '\\', ':']) < 0 && file != ".lark";

    private static string UnitIsNoRule(string unit) => $"recover.unit names '{unit}', which is no rule of the grammar (FBL 0.9 4.9).";

    /// <summary>A grammar file's text, read at the sharing the loader reads an FBL document with, without its BOM.</summary>
    private static string ReadText(string path)
    {
        var bytes = FblDocumentLoader.ReadShared(path);
        var bom = bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, bom, bytes.Length - bom);
    }

    private static string OneLine(string message) => string.Join(" ", message.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static LoadDiagnostic Error(string pointer, string message) => new(pointer, LoadSeverity.Error, message);

    /// <summary>Serves an <c>%import</c> from a grammar file beside the FBL document, and from nowhere else (FBL 0.9 §4.9).</summary>
    private sealed class BesideLoader(string? folder) : IGrammarLoader
    {
        public bool TryLoad(string? basePath, string grammarPath, out string fullPath, out string text)
        {
            fullPath = "";
            text = "";
            if (folder is null || !IsBesideFile(grammarPath)) return false;
            var path = Path.Combine(folder, grammarPath);
            if (!File.Exists(path)) return false;
            fullPath = path;
            text = ReadText(path);
            return true;
        }
    }
}

/// <summary>A body read unit by unit (FBL 0.9 §4.9): the units, in order, the text skipped, and the tokens cut outside it.</summary>
internal sealed record GrammarUnits(IReadOnlyList<object?> Units, IReadOnlyList<GrammarSkip> Skips, IReadOnlyDictionary<int, Token> Tokens);

/// <summary>Skipped text, in UTF-16 offsets: from the unit's first token, through <paramref name="Stop"/>, where reading stopped, to its end.</summary>
internal readonly record struct GrammarSkip(int Start, int Stop, int End);
