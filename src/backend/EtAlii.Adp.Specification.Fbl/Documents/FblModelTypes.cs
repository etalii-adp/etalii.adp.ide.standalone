using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>A loaded FBL document (FBL §2.1): its version and its bindings by name.</summary>
public sealed class FblDocument
{
    public required string Version { get; init; }

    public required IReadOnlyDictionary<string, FblBinding> Bindings { get; init; }
}

public enum Family
{
    Yaml,
    Json,
    Xml,
    Lines,
    Blocks,
    Bullets,

    /// <summary>FBL 0.9 §4.9: a body that the context-free grammar the binding carries reads.</summary>
    Grammar,
}

/// <summary>One binding (FBL §3): how one format maps to a language's model.</summary>
public sealed class FblBinding
{
    public required string Name { get; init; }

    public string? Title { get; init; }

    public required Claims Claims { get; init; }

    public required BodySettings Body { get; init; }

    /// <summary>Null for a declared reader; the plugin otherwise (FBL §11).</summary>
    public PluginReader? Plugin { get; init; }

    /// <summary>Null when the body may be written; the reason (possibly empty) when it is read-only.</summary>
    public string? ReadOnly { get; init; }

    internal TextDefaults Text { get; init; } = new();

    internal HeaderSettings? Header { get; init; }

    public string? Comment { get; init; }

    public bool ReportUnmatched { get; init; }

    internal IReadOnlyList<BlockRule> Blocks { get; init; } = [];

    /// <summary>The sections of a <c>bullets</c> body (FBL 0.7 §4.8), each found by its first matching heading.</summary>
    internal IReadOnlyList<SectionRule> Sections { get; init; } = [];

    public IReadOnlyList<Rule> Elements { get; init; } = [];

    public IReadOnlyList<Rule> Relations { get; init; } = [];

    internal RegistrationSettings Registration { get; init; } = new();

    public TemplateSettings? Template { get; init; }

    /// <summary>Elements before relations, in binding order: the order rules are offered an entry (FBL §5.1).</summary>
    public IEnumerable<Rule> AllRules => Elements.Concat(Relations);

    /// <summary>
    /// The check of FBL 0.13 §14.1 step 6 that needs the tool type's attribute types: a slot with <c>split</c> stands only on
    /// an attribute that <paramref name="many"/>, given the rule's type and the attribute's name, says is <c>many</c>. Each
    /// refusal is a problem at the slot's <c>split</c>; none when every such attribute is <c>many</c>.
    /// </summary>
    public IReadOnlyList<LoadDiagnostic> CheckManyAttributes(Func<string, string, bool> many)
    {
        var problems = new List<LoadDiagnostic>();
        var rules = Elements.Select((rule, i) => (rule, $"elements/{i}")).Concat(Relations.Select((rule, i) => (rule, $"relations/{i}")));
        foreach ((Rule rule, string at) in rules)
        {
            foreach ((string name, AttributeBinding binding) in rule.Attributes)
            {
                if (binding.Split is null || many(rule.Type, name)) continue;
                problems.Add(new LoadDiagnostic($"/bindings/{JsonPointer.Token(Name)}/{at}/attributes/{JsonPointer.Token(name)}/split", LoadSeverity.Error,
                    $"The attribute {name} of a {rule.Type} is single-valued, so the items its split cuts have nothing to hold them (FBL 0.13 14.1)."));
            }
        }
        return problems;
    }

    // The binding as each reading sees it, made once per origin (FBL 0.10 §5.1, §8.2).
    private readonly Dictionary<string, FblBinding> _readings = new(StringComparer.Ordinal);

    /// <summary>
    /// The binding as the reading of <paramref name="origin"/> sees it (FBL 0.10 §5.1, §8.2): only the rules whose
    /// <c>origins</c> admit it, a rule without <c>origins</c> being every reading's, and the resource capture
    /// <c>registration.resource</c> gives that origin. Null, a reading that names no origin, sees every rule and the
    /// binding's one <c>capture</c>, as before 0.10.
    /// </summary>
    public FblBinding ForOrigin(string? origin)
    {
        if (Unfiltered is { } written) return written.ForOrigin(origin);
        if (origin is null) return this;
        lock (_readings)
        {
            if (_readings.TryGetValue(origin, out var known)) return known;
            var admitted = new Func<Rule, bool>(rule => rule.Origins is null || rule.Origins.Contains(origin));
            var reading = new FblBinding
            {
                Name = Name,
                Title = Title,
                Claims = Claims,
                Body = Body,
                Plugin = Plugin,
                ReadOnly = ReadOnly,
                Text = Text,
                Header = Header,
                Comment = Comment,
                ReportUnmatched = ReportUnmatched,
                Blocks = Blocks,
                Sections = Sections,
                Elements = [.. Elements.Where(admitted)],
                Relations = [.. Relations.Where(admitted)],
                Registration = Registration.ForOrigin(origin),
                Template = Template,
                Unfiltered = this,
            };
            _readings[origin] = reading;
            return reading;
        }
    }

    /// <summary>The binding as written, of which this is the reading of one origin (<see cref="ForOrigin"/>); null when this is it.</summary>
    private FblBinding? Unfiltered { get; init; }
}

public sealed class Claims
{
    public IReadOnlyList<string> Extensions { get; init; } = [];

    public IReadOnlyList<string> Names { get; init; } = [];

    public bool Shared { get; init; }

    public bool RegistrationOnly { get; init; }

    internal Marker? Marker { get; init; }

    public IReadOnlyList<string> Suggest { get; init; } = [];

    public IReadOnlyList<string> Origins { get; init; } = [];

    /// <summary>Per origin: whether a bare file opens as it, and what suggests it.</summary>
    internal IReadOnlyDictionary<string, ReadingClaim> Readings { get; init; } = new Dictionary<string, ReadingClaim>();
}

internal sealed record ReadingClaim(bool Bare, IReadOnlyList<string> Suggest);

/// <summary>A marker (FBL §12.2): exactly one of a root key, a first-line prefix or a pattern.</summary>
internal sealed record Marker(string? RootKey, JsonElement? RootValue, string? FirstLine, string? Pattern, int Lines);

public sealed class BodySettings
{
    public bool IsFolder { get; init; }

    public Family? Family { get; init; }

    public IReadOnlyList<Family> AlsoRead { get; init; } = [];

    public IReadOnlyList<string> RecogniseAll { get; init; } = [];

    public IReadOnlyList<string> RecogniseAny { get; init; } = [];

    public IReadOnlyList<string> RecogniseNone { get; init; } = [];

    public IReadOnlyList<FileRule> Files { get; init; } = [];

    public IReadOnlyList<string> Ignore { get; init; } = [];

    /// <summary>The grammar of a body of the <c>grammar</c> family (FBL 0.9 §4.9); null for every other family.</summary>
    internal GrammarBody? Grammar { get; init; }

    /// <summary>How long, in milliseconds, a watched folder must be quiet before it is read again (FBL §10.3).</summary>
    public int Settle { get; init; } = DefaultSettle;

    /// <summary>The <c>settle</c> of a folder whose binding states none (FBL §10.3).</summary>
    public const int DefaultSettle = 400;
}

public sealed record FileRule(string? Name, string Glob, Family? Family);

/// <summary>
/// A body's grammar (FBL 0.9 §3.2, §4.9): its text, or the name of the <c>.lark</c> file beside the FBL document that
/// holds it; its start rule; and how a body that does not parse is read in part. <see cref="Built"/> is the parser,
/// built once as the binding loads.
/// </summary>
internal sealed class GrammarBody
{
    public string? Text { get; init; }

    public string? File { get; init; }

    public string Start { get; init; } = "start";

    public GrammarRecover? Recover { get; init; }

    public GrammarBuild? Built { get; set; }
}

/// <summary><c>body.recover</c> (FBL 0.9 §4.9): the rule a body is read unit by unit by, and the terminals a skip ends at.</summary>
internal sealed record GrammarRecover(string Unit, IReadOnlyList<string> Sync);

public sealed record PluginReader(string Plugin, string? Version)
{
    /// <summary>The reader's <c>args</c> (FBL §11.1), an object handed to every <c>read</c>; null when the binding gives none.</summary>
    public JsonElement? Args { get; init; }
}

internal sealed class TextDefaults
{
    public string Newline { get; init; } = "\n";

    /// <summary>Spaces per step, or 0 for a tab.</summary>
    public int Indent { get; init; } = 2;

    public bool SequenceFlush { get; init; }

    public QuoteStyle Quote { get; init; } = QuoteStyle.Double;
}

internal sealed record HeaderSettings(string? Key, JsonElement? Value, string? Line, bool Required);

internal sealed record BlockRule(string Name, string Line, IReadOnlyList<string>? Within, bool CaseInsensitive, string? View);

/// <summary>A section of a <c>bullets</c> body (FBL 0.7 §4.8): its name, and the expression its heading's text matches.</summary>
internal sealed record SectionRule(string Name, string Heading, bool CaseInsensitive);

/// <summary>An element rule or a relation rule (FBL §5.1, §5.4).</summary>
public sealed partial class Rule
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool IsRelation { get; init; }

    public string? At { get; init; }

    public string? Line { get; init; }

    public IReadOnlyList<string>? Within { get; init; }

    public bool Opens { get; init; }

    public bool CaseInsensitive { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public string? When { get; init; }

    internal IdBinding? Id { get; init; }

    internal ParentBinding? Parent { get; init; }

    /// <summary>The attribute bindings in the order the binding writes them.</summary>
    public IReadOnlyList<KeyValuePair<string, AttributeBinding>> Attributes { get; init; } = [];

    public AttributeBinding? Source { get; init; }

    public AttributeBinding? Target { get; init; }

    internal InsertSettings? Insert { get; init; }

    internal RemoveSettings? Remove { get; init; }

    public bool SnapshotUndo { get; init; }

    public string? ReadOnly { get; init; }

    /// <summary>The readings the rule is read for, as origins (FBL 0.10 §5.1); null for every reading.</summary>
    public IReadOnlyList<string>? Origins { get; init; }

    /// <summary>
    /// The programs of this rule's expressions, compiled once as the binding loads (FBL §14.1 step 8),
    /// by the context they were compiled for and their text. A reading uses them in place of compiling
    /// the expression again.
    /// </summary>
    private readonly Dictionary<(CelContext Context, string Expression), CelProgram> _programs = [];

    internal void Keep(CelContext context, string expression, CelProgram program) => _programs[(context, expression)] = program;

    /// <summary>The program <paramref name="expression"/> was compiled to for <paramref name="context"/> as the binding loaded; null when it was not.</summary>
    internal CelProgram? Program(CelContext context, string expression) => _programs.GetValueOrDefault((context, expression));

    public AttributeBinding? Attribute(string name) => Attributes.FirstOrDefault(a => a.Key == name).Value;

    /// <summary>
    /// The capture a rule's <c>at</c> ends in (<c>/resources/jobs/{job}</c> ends in <c>job</c>), which names each entry it
    /// reads by its key in a mapping; null when its last segment is not a capture (FBL 0.14 §6.2).
    /// </summary>
    internal string? MemberCapture => At is { } at && MemberSegmentPattern().Match(at) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>
    /// The attribute bound to <see cref="MemberCapture"/>, whose value an add gives as the new member's key, and its
    /// name; null when the rule reads no member or binds no attribute to its capture (FBL 0.14 §6.2, §14.1).
    /// </summary>
    internal string? MemberKeyAttribute => MemberCapture is { } capture
        ? Attributes.FirstOrDefault(a => a.Value.Kind is SlotKind.Capture c && c.Name == capture).Key
        : null;

    /// <summary>The last segment of an <c>at</c>, when it is a capture.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"/\{([^}/]+)\}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex MemberSegmentPattern();

    /// <summary>A placeholder of an <c>emit</c> text: an attribute's name, or <c>id</c>, in braces.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PlaceholderPattern();

    /// <summary>
    /// The order a new entry's keys or attributes are written in, as the names they are written
    /// under: <c>insert.keys</c>, or, for a rule that writes its entry from <c>emit</c>, the order
    /// of the text's placeholders. A key or attribute added to an existing entry takes its place
    /// in this order (FBL §6.1). Empty for a rule that cannot insert.
    /// </summary>
    public IReadOnlyList<string> WireOrder()
    {
        if (Insert is null) return [];
        if (Insert.Keys.Count > 0) return Insert.Keys;
        if (Insert.Emit is not { } emit) return [];
        var order = new List<string>();
        foreach (System.Text.RegularExpressions.Match placeholder in PlaceholderPattern().Matches(emit))
        {
            var name = placeholder.Groups[1].Value;
            var slot = name == "id" ? Id?.From : Attribute(name);
            var wire = slot?.Kind switch
            {
                SlotKind.Key(var key) => key,
                SlotKind.XmlAttribute(var attribute) => attribute,
                _ => null,
            };
            if (wire is not null && !order.Contains(wire)) order.Add(wire);
        }
        return order;
    }

    /// <summary>
    /// The order keys are written in (FBL §6.1): the <see cref="WireOrder"/>, then the keys of the rule's
    /// bindings in binding order, the id's, the source's, the target's and the attributes'. With
    /// <paramref name="child"/>, the keys of the bindings under that <c>child</c> mapping only.
    /// </summary>
    public List<string> KeyOrder(string? child)
    {
        var order = new List<string>();
        if (child is null) order.AddRange(WireOrder());
        Add(Id?.From);
        Add(Source);
        Add(Target);
        foreach ((_, AttributeBinding binding) in Attributes) Add(binding);
        return order;

        void Add(Slot? slot)
        {
            if (slot is { Kind: SlotKind.Key(var key) } && slot.Child == child && !order.Contains(key)) order.Add(key);
        }
    }
}

internal sealed record IdBinding(Slot? From, string? SidecarKey);

internal sealed record ParentBinding(IReadOnlyList<string> Rules, string? Slot);

/// <summary>
/// A slot (FBL §5.2): exactly one place a value is read from and written to, its <see cref="Kind"/>;
/// <see cref="Child"/>, <see cref="Word"/> and <see cref="Flag"/> qualify it.
/// </summary>
public record Slot
{
    public required SlotKind Kind { get; init; }

    public string? Child { get; init; }

    public string? Word { get; init; }

    public bool Flag { get; init; }

    /// <summary>A <c>{node}</c> slot's <c>match</c> (FBL 0.9 §5.2): its named group <c>value</c> is the value, and its span the value's.</summary>
    public string? Match { get; init; }

    /// <summary>A <c>{node}</c> slot's <c>escape</c> (FBL 0.11 §5.2): how a quoted value escapes its quotes.</summary>
    internal SlotEscape? Escape { get; init; }

    /// <summary>A <c>{node}</c> slot's <c>split</c> (FBL 0.13 §4.9): the text its value is cut into items at.</summary>
    public string? Split { get; init; }

    /// <summary>The text written between two items (FBL 0.12 §5.2), <c>", "</c> when the binding gives none.</summary>
    public string? Separator { get; init; }

    public override string ToString() => Kind.ToString();
}

/// <summary>The kinds of slot (FBL §3.3, §5.2), a closed set: a slot is exactly one of them.</summary>
public abstract record SlotKind
{
    private SlotKind()
    {
    }

    /// <summary>A key of a tree entry (yaml, json).</summary>
    public sealed record Key(string Name) : SlotKind
    {
        public override string ToString() => $"key {Name}";
    }

    /// <summary>An attribute of an xml element.</summary>
    public sealed record XmlAttribute(string Name) : SlotKind
    {
        public override string ToString() => $"attribute {Name}";
    }

    /// <summary>The text of an xml element, or of its child that <see cref="Slot.Child"/> selects.</summary>
    public sealed record Text : SlotKind
    {
        public override string ToString() => "text";
    }

    /// <summary>A named group of a lines or blocks rule's expression.</summary>
    public sealed record Group(string Name) : SlotKind
    {
        public override string ToString() => $"group {Name}";
    }

    /// <summary>The matching slot of the enclosing entry.</summary>
    public sealed record Parent(string Name) : SlotKind
    {
        public override string ToString() => $"parent {Name}";
    }

    /// <summary>A capture of the rule's selector.</summary>
    public sealed record Capture(string Name) : SlotKind
    {
        public override string ToString() => $"capture {Name}";
    }

    /// <summary>The continuation of a <c>bullets</c> item (FBL 0.7 §4.8): its lines before its first nested item.</summary>
    public sealed record Continuation : SlotKind
    {
        public override string ToString() => "continuation";
    }

    /// <summary>The node or token of the grammar family that a selector relative to the entry reaches (FBL 0.9 §4.9).</summary>
    public sealed record Node(string Selector) : SlotKind
    {
        public override string ToString() => $"node {Selector}";
    }

    /// <summary>A value computed by CEL, read-only.</summary>
    public sealed record Value(string Expression) : SlotKind
    {
        public override string ToString() => "value";
    }
}

/// <summary>An attribute binding (FBL §5.2): a slot with the options that decide reading and writing.</summary>
public sealed record AttributeBinding : Slot
{
    internal EmptyRule Empty { get; init; } = EmptyRule.Keep;

    /// <summary>Per family name: what setting a value whose slot is absent does.</summary>
    internal IReadOnlyDictionary<string, AbsentRule> Absent { get; init; } = new Dictionary<string, AbsentRule>();

    public JsonElement? Default { get; init; }

    /// <summary>Null for "shortest", else the number of decimals.</summary>
    public int? Decimals { get; init; }

    public bool KeepTimePrecision { get; init; }

    internal WrittenStyle? Style { get; init; }

    internal ReferenceBinding? Reference { get; init; }

    /// <summary>Wire value to model value, in document order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Map { get; init; }

    public Slot? Override { get; init; }

    public bool HtmlParagraphs { get; init; }

    internal CreateChild? Create { get; init; }

    public string? ReadOnly { get; init; }

    /// <summary>yaml, json (FBL 0.5): what the slot reads from a scalar; null to let the attribute's type decide.</summary>
    internal ReadAs? Read { get; init; }

    /// <summary>yaml, read as typed (FBL 0.5): whether a quoted scalar converts from its written text; null for not given, which does not.</summary>
    internal QuotedRule? Quoted { get; init; }

    /// <summary>yaml, json (FBL 0.5): what the attribute is when its value does not convert, keeping the entry readable.</summary>
    internal FallbackBinding? Fallback { get; init; }

    /// <summary>
    /// A second slot read only when the attribute's own slot is absent (FBL 0.10 §5.2), and removed whenever the
    /// attribute is written: the reverse of <see cref="Slot"/>'s <c>override</c>. Null when the binding gives none.
    /// </summary>
    public Slot? Secondary { get; init; }

    /// <summary>The sentence a set or an add refused by <c>empty: "refuse"</c> is refused with (FBL 0.10 §5.2, §6.4); null for the host's own.</summary>
    public string? EmptyRefusal { get; init; }

    /// <summary>The sentence a set refused by an <c>absent</c> rule is refused with (FBL 0.10 §5.2, §6.4); null for the host's own.</summary>
    public string? AbsentRefusal { get; init; }
}

internal sealed record ReferenceBinding(IReadOnlyList<string> To, string By);

/// <summary>An attribute binding's <c>fallback</c> (FBL 0.5 §5.2): its value, and whether <c>fbl.unconverted-value</c> is reported.</summary>
internal sealed record FallbackBinding(JsonElement Value, bool Report);

/// <summary>
/// How a missing child holding the value is written. xml: the element's text and its place. yaml
/// (FBL 0.4): <see cref="AtEnd"/>, each missing level of the mapping <c>child</c> reaches created at
/// the end of the level above it.
/// </summary>
internal sealed record CreateChild(string Emit, ChildPlace Place, string? Before, bool AtEnd = false)
{
    /// <summary>
    /// grammar (FBL 0.11 §5.2): the attributes of the same rule, nearest first, whose parts precede an absent part, which
    /// is written right after the first of them that is present; null when the binding gives none.
    /// </summary>
    public IReadOnlyList<string>? After { get; init; }
}

/// <summary>
/// A <c>{node}</c> slot's <c>escape</c> (FBL 0.11 §5.2): reading replaces <paramref name="With"/> before
/// <paramref name="Quote"/> or before itself by that character and keeps it before any other; writing escapes both.
/// </summary>
internal sealed record SlotEscape(char With, char Quote)
{
    public string Read(string text)
    {
        if (text.IndexOf(With) < 0) return text;
        var read = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == With && i + 1 < text.Length && (text[i + 1] == Quote || text[i + 1] == With)) i++;
            read.Append(text[i]);
        }
        return read.ToString();
    }

    public string Write(string value)
    {
        var written = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == With || c == Quote) written.Append(With);
            written.Append(c);
        }
        return written.ToString();
    }
}

internal sealed class InsertSettings
{
    public required InsertPlace Place { get; init; }

    public string? Container { get; init; }

    public CreateContainer? Create { get; init; }

    public IReadOnlyList<string> Keys { get; init; } = [];

    public string? Emit { get; init; }

    public string? Skeleton { get; init; }

    public string? When { get; init; }

    /// <summary>
    /// The keys and values every new entry is given as they are (FBL 0.8 §6.2), in the constant's member
    /// order: a string, a <see cref="double"/>, a <see cref="bool"/> or null. Null when the insert has none.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>>? Constant { get; init; }

    /// <summary>The sentence an add is refused with when <see cref="When"/> does not hold (FBL 0.10 §6.2, §6.4); null for the host's own.</summary>
    public string? WhenRefusal { get; init; }

    /// <summary>The sentence an add is refused with when the container is missing and there is no <see cref="Create"/> (FBL 0.10 §6.2, §6.4); null for the host's own.</summary>
    public string? ContainerRefusal { get; init; }

    /// <summary>The sentence an add of a new member is refused with when it gives no key or the empty text (FBL 0.14 §6.2, §6.4); null for the host's own.</summary>
    public string? KeyRefusal { get; init; }

    /// <summary>The sentence an add of a new member is refused with when its key is one the mapping already holds (FBL 0.14 §6.2, §6.4); null for the host's own.</summary>
    public string? DuplicateRefusal { get; init; }
}

/// <summary>How a missing container is created: where, with the key or selector <see cref="At"/> names, and its own text.</summary>
internal sealed record CreateContainer(ContainerPlace At, string? Argument, string? Text);

/// <summary>
/// What goes with a removed entry. <see cref="RemoveEmptyLevels"/> (FBL 0.4) implies
/// <see cref="RemoveContainerWhenEmpty"/>, and removes the levels above the container that it leaves empty too.
/// </summary>
internal sealed record RemoveSettings(IReadOnlyList<string> Cascade, bool RemoveContainerWhenEmpty, bool RemoveEmptyLevels = false);

internal sealed class RegistrationSettings
{
    public IReadOnlyList<string> Headers { get; init; } = [];

    public bool CreateOnFirstPlacement { get; init; }

    /// <summary>The blocks the binding declares (FBL 0.7 §8.1), in the order it declares them, which is the order they are written in (§8.4).</summary>
    public IReadOnlyList<RegistrationBlockDeclaration> Blocks { get; init; } = [];

    public string? ResourceCapture { get; init; }

    /// <summary>
    /// <c>registration.resource.byOrigin</c> (FBL 0.10 §8.2): the capture each origin's reading selects its resource
    /// by, null for an origin that selects none; null when the binding gives one <see cref="ResourceCapture"/> for all.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? ResourceByOrigin { get; init; }

    public string? LegacyLayout { get; init; }

    /// <summary>These settings as the reading of <paramref name="origin"/> sees them: its own resource capture, or none (FBL 0.10 §8.2).</summary>
    public RegistrationSettings ForOrigin(string origin) => ResourceByOrigin is not { } byOrigin
        ? this
        : new RegistrationSettings
        {
            Headers = Headers,
            CreateOnFirstPlacement = CreateOnFirstPlacement,
            Blocks = Blocks,
            ResourceCapture = byOrigin.GetValueOrDefault(origin),
            LegacyLayout = LegacyLayout,
        };
}

public sealed record TemplateSettings(string Text, IReadOnlyDictionary<string, string> ByOrigin);
