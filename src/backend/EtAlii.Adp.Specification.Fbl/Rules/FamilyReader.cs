namespace EtAlii.Adp.Specification.Fbl.Rules;

using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Text;

/// <summary>
/// A node a rule can match (FBL §4.1.1): its own span, its line span when it starts and ends its
/// lines, and the entries it contains.
/// </summary>
internal abstract class Entry
{
    public required Span Own { get; set; }

    /// <summary>The line span (FBL §4.1.1), leading comments included; null when the entry shares a line.</summary>
    public Span? LineSpan { get; set; }

    public Entry? Parent { get; set; }

    public List<Entry> Children { get; } = [];

    /// <summary>The key of a member, the name of an xml element; null for a sequence item or a statement.</summary>
    public string? Name { get; init; }

    /// <summary>The byte column of the entry's first byte on its line.</summary>
    public int Indent { get; init; }

    /// <summary>The span a removal takes: the line span when there is one, else the own span.</summary>
    public Span RemovalSpan => LineSpan ?? Own;

    public override string ToString() => $"{GetType().Name} {Name} {Own}";
}

/// <summary>An entry a rule's selector or line matched, with the captures or groups the match bound.</summary>
internal sealed record Candidate(Rule? Rule, BlockRule? Block, Entry Entry, IReadOnlyDictionary<string, string> Captures);

/// <summary>A word of a lines or blocks group (FBL §4.6), with its span (quotes included) and its text (quotes excluded).</summary>
internal sealed record Word(string Text, Span Span, bool Quoted);

/// <summary>What reading one slot of one entry gave.</summary>
internal sealed record SlotRead(object? Value, Span? Span, bool Present, bool Writable, string? Reason = null)
{
    public static SlotRead Absent { get; } = new(null, null, false, true);

    public static SlotRead ReadOnlyAbsent(string reason) => new(null, null, false, false, reason);

    /// <summary>The family's own node for the slot (a yaml member, an xml attribute, a lines group), for writing.</summary>
    public object? Node { get; init; }

    /// <summary>For a group read as words: every word of the group, so references inside it can be rewritten one by one.</summary>
    public IReadOnlyList<Word>? Words { get; init; }

    /// <summary>The quote the value is written in on the wire, when the span includes one.</summary>
    public string? Quote { get; init; }

    /// <summary>The value as written, before maps or conversion, for keeping a map's wire value.</summary>
    public string? Wire { get; init; }
}

/// <summary>An element or relation as the engine keeps it: the public element plus where each value lives.</summary>
internal sealed class ReadElement
{
    public required Rule Rule { get; init; }

    public required Candidate Candidate { get; init; }

    public Entry Entry => Candidate.Entry;

    public string Id { get; set; } = "";

    public bool IdStored { get; set; }

    public SlotRead? IdRead { get; set; }

    /// <summary>The value references name this element by: its stored id, or the attribute its own rules reference it by.</summary>
    public string Key { get; set; } = "";

    /// <summary>The attribute whose value is <see cref="Key"/>, when it is an attribute.</summary>
    public string? KeyAttribute { get; set; }

    public Dictionary<string, SlotRead> Slots { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.Ordinal);

    public ReadElement? Parent { get; set; }

    public SlotRead? SourceRead { get; set; }

    public SlotRead? TargetRead { get; set; }

    public ReadElement? SourceElement { get; set; }

    public ReadElement? TargetElement { get; set; }

    public int Line { get; set; }

    public bool IsRelation => Rule.IsRelation;

    public override string ToString() => $"{Rule.Name} {Id}";
}

/// <summary>
/// One family's lossless reading of a body (FBL §4) and the splices it writes (FBL §6). The engine
/// (<see cref="BodyReading"/>) does what is the same for every family: rule precedence, ids,
/// references, containment, findings and refusals; the planner does the shape of an edit.
/// </summary>
internal abstract class FamilyReader(BodyText text, FblBinding binding, FblOptions options)
{
    private readonly Dictionary<string, BoundedRegex> _regexes = new(StringComparer.Ordinal);

    public BodyText Text { get; } = text;

    public FblBinding Binding { get; } = binding;

    public FblOptions Options { get; } = options;

    /// <summary>Where and why the body is unreadable (FBL §7.5), or null.</summary>
    public (int Offset, string Message)? Unreadable { get; protected set; }

    public List<Finding> Findings { get; } = [];

    public abstract string FamilyName { get; }

    /// <summary>Every entry in document order (FBL §4.1.3).</summary>
    public abstract IReadOnlyList<Entry> Entries { get; }

    /// <summary>Builds the lossless reading; sets <see cref="Unreadable"/> when the body is not well-formed.</summary>
    public abstract void Parse();

    /// <summary>
    /// The leaf nodes of the reading (keys, scalars, statements, tags, comments), in body order.
    /// Every byte outside them is trivia (FBL §4.1): the invariant every reader is held to, checked
    /// with <see cref="IsTrivia"/> on each gap.
    /// </summary>
    public abstract IReadOnlyList<Span> Leaves { get; }

    /// <summary>Whether the bytes of <paramref name="gap"/> are trivia of this family: whitespace, line endings, punctuation.</summary>
    public abstract bool IsTrivia(Span gap);

    /// <summary>The gaps between leaves that are not trivia: empty when the reading accounts for every byte.</summary>
    public IReadOnlyList<Span> Unaccounted()
    {
        var gaps = new List<Span>();
        var position = Text.BomLength;
        foreach (var leaf in Leaves.OrderBy(l => l.Start))
        {
            if (leaf.Start < position) continue;
            if (leaf.Start > position && !IsTrivia(new Span(position, leaf.Start))) gaps.Add(new Span(position, leaf.Start));
            position = Math.Max(position, leaf.End);
        }
        if (position < Text.Length && !IsTrivia(new Span(position, Text.Length))) gaps.Add(new Span(position, Text.Length));
        return gaps;
    }

    /// <summary>Whether a byte range holds only whitespace and line endings.</summary>
    protected bool IsWhitespace(Span span)
    {
        for (var i = span.Start; i < span.End; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
        }
        return true;
    }

    public abstract IEnumerable<Candidate> Candidates(Rule rule);

    public virtual IEnumerable<Candidate> BlockCandidates(BlockRule block) => [];

    /// <summary>Whether <paramref name="within"/> admits <paramref name="entry"/>, given what claimed the entries around it (blocks).</summary>
    public virtual bool Admits(IReadOnlyList<string>? within, Entry entry, Func<Entry, string?> claimedBy) => true;

    /// <summary>The entries that structurally enclose <paramref name="entry"/>, nearest first.</summary>
    public virtual IEnumerable<Entry> Enclosing(Entry entry)
    {
        for (var parent = entry.Parent; parent is not null; parent = parent.Parent) yield return parent;
    }

    /// <summary>The entry as CEL sees it (FBL §4.1.4).</summary>
    public abstract object? CelValue(Candidate candidate);

    /// <summary>The variables a rule's CEL besides <c>entry</c>, <c>parent</c>, <c>line</c> and <c>registration</c>: <c>path</c> or <c>groups</c>.</summary>
    public abstract (string Name, object? Value) CelExtra(Candidate candidate);

    /// <summary>Reads a slot that is the family's own: a key, an attribute, text, a group, a capture.</summary>
    public abstract SlotRead Read(Candidate candidate, Slot slot);

    /// <summary>Reads the slot named <paramref name="name"/> of an enclosing entry, for a <c>parent</c> slot naming no attribute.</summary>
    public abstract SlotRead ReadRaw(Entry entry, string name);

    /// <summary>The header check (FBL §5.6): whether the mark is there with the right value.</summary>
    public abstract bool HeaderHolds(HeaderSettings header);

    /// <summary>Findings about entries no rule claimed, once reading is done.</summary>
    public virtual void AfterRead(Func<Entry, string?> claimedBy)
    {
    }

    // ---- writing (FBL §6) ----

    /// <summary>The text that replaces a value's span: the new value in the value's own style when it fits (FBL §6.3).</summary>
    public abstract string Format(SlotRead read, AttributeBinding? binding, object? value);

    /// <summary>Writes <paramref name="changes"/> to <paramref name="element"/>; the engine has already refused what must be refused.</summary>
    public abstract void Write(Plan plan, ReadElement element, IReadOnlyList<SlotChange> changes);

    public abstract void Insert(Plan plan, InsertRequest request);

    public abstract void Remove(Plan plan, ReadElement element, IReadOnlySet<ReadElement> removed);

    // ---- shared helpers ----

    public string NewlineAt(int offset) => Text.NewlineAt(offset, Binding.Text.Newline);

    public BoundedRegex Regex(string expression, bool caseInsensitive)
    {
        var key = (caseInsensitive ? "i:" : "s:") + expression;
        if (!_regexes.TryGetValue(key, out var regex))
        {
            regex = new BoundedRegex(expression, caseInsensitive, Options.RegexTimeout);
            _regexes[key] = regex;
        }
        return regex;
    }

    public SourceLocation Locate(Span span)
    {
        var (line, column) = Text.Position(span.Start);
        return new SourceLocation(Options.FileName, line, column, Text.CodePoints(span.Start, span.End));
    }

    public void Report(string code, FindingSeverity severity, string message, Span? span) =>
        Findings.Add(new Finding(code, severity, message, span is { } s ? Locate(s) : null));

    /// <summary>The indentation one step deeper than <paramref name="indent"/>, by FBL §6.3's step.</summary>
    public string Indentation(int columns) => new(IndentCharacter, columns);

    public virtual char IndentCharacter => Binding.Text.Indent == 0 ? '\t' : ' ';

    /// <summary>
    /// The indentation step (FBL §6.3): the difference between the indentation of the first parent
    /// and child pair in document order, else the binding's <c>text.indent</c>.
    /// </summary>
    public virtual int Step
    {
        get
        {
            foreach (var entry in Entries)
            {
                if (entry.Parent is { } parent && entry.Indent > parent.Indent && entry.LineSpan is not null && parent.LineSpan is not null)
                {
                    return entry.Indent - parent.Indent;
                }
            }
            return Binding.Text.Indent == 0 ? 1 : Binding.Text.Indent;
        }
    }

    /// <summary>The bytes of the line holding <paramref name="offset"/> before it, when they are all whitespace.</summary>
    public bool OnlyWhitespaceBefore(int offset)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        for (var i = Math.Max(line.Start, Text.BomLength); i < offset; i++)
        {
            if (Text.Bytes[i] is not ((byte)' ' or (byte)'\t')) return false;
        }
        return true;
    }

    /// <summary>Whether only whitespace (and a comment, when <paramref name="comment"/> is given) follows <paramref name="offset"/> on its line.</summary>
    public bool OnlyTriviaAfter(int offset, byte? comment = null)
    {
        var line = Text.Lines[Text.LineIndexAt(offset)];
        for (var i = offset; i < line.ContentEnd; i++)
        {
            var b = Text.Bytes[i];
            if (comment is { } c && b == c && (i == offset || Text.Bytes[i - 1] is (byte)' ' or (byte)'\t')) return true;
            if (b is not ((byte)' ' or (byte)'\t')) return false;
        }
        return true;
    }
}

/// <summary>One attribute to write, after the engine's checks: its binding, how it is read now, and its new value.</summary>
internal sealed record SlotChange(string Attribute, AttributeBinding Binding, Rule Rule, SlotRead Read, object? Value, bool IsEmpty);

/// <summary>What adding an element or relation needs to know (FBL §6.2).</summary>
internal sealed record InsertRequest(Rule Rule, string? Id, IReadOnlyDictionary<string, object?> Values, ReadElement? Parent, ReadElement? Source, ReadElement? Target);

/// <summary>Thrown inside planning when a change cannot be made; the edit is refused with its message (FBL §6.4).</summary>
internal sealed class RefusedException(string reason) : Exception(reason);
