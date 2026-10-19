using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;

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

    public TextDefaults Text { get; init; } = new();

    public HeaderSettings? Header { get; init; }

    public string? Comment { get; init; }

    public bool ReportUnmatched { get; init; }

    public IReadOnlyList<BlockRule> Blocks { get; init; } = [];

    public IReadOnlyList<Rule> Elements { get; init; } = [];

    public IReadOnlyList<Rule> Relations { get; init; } = [];

    public RegistrationSettings Registration { get; init; } = new();

    public TemplateSettings? Template { get; init; }

    /// <summary>Elements before relations, in binding order: the order rules are offered an entry (FBL §5.1).</summary>
    public IEnumerable<Rule> AllRules => Elements.Concat(Relations);
}

public sealed class Claims
{
    public IReadOnlyList<string> Extensions { get; init; } = [];

    public IReadOnlyList<string> Names { get; init; } = [];

    public bool Shared { get; init; }

    public bool RegistrationOnly { get; init; }

    public Marker? Marker { get; init; }

    public IReadOnlyList<string> Suggest { get; init; } = [];

    public IReadOnlyList<string> Origins { get; init; } = [];

    /// <summary>Per origin: whether a bare file opens as it, and what suggests it.</summary>
    public IReadOnlyDictionary<string, ReadingClaim> Readings { get; init; } = new Dictionary<string, ReadingClaim>();
}

public sealed record ReadingClaim(bool Bare, IReadOnlyList<string> Suggest);

/// <summary>A marker (FBL §12.2): exactly one of a root key, a first-line prefix or a pattern.</summary>
public sealed record Marker(string? RootKey, JsonElement? RootValue, string? FirstLine, string? Pattern, int Lines);

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

    /// <summary>How long, in milliseconds, a watched folder must be quiet before it is read again (FBL §10.3).</summary>
    public int Settle { get; init; } = DefaultSettle;

    /// <summary>The <c>settle</c> of a folder whose binding states none (FBL §10.3).</summary>
    public const int DefaultSettle = 400;
}

public sealed record FileRule(string? Name, string Glob, Family? Family);

public sealed record PluginReader(string Plugin, string? Version);

public sealed class TextDefaults
{
    public string Newline { get; init; } = "\n";

    /// <summary>Spaces per step, or 0 for a tab.</summary>
    public int Indent { get; init; } = 2;

    public bool SequenceFlush { get; init; }

    public QuoteStyle Quote { get; init; } = QuoteStyle.Double;
}

public sealed record HeaderSettings(string? Key, JsonElement? Value, string? Line, bool Required);

public sealed record BlockRule(string Name, string Line, IReadOnlyList<string>? Within, bool CaseInsensitive, string? View);

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

    public IdBinding? Id { get; init; }

    public ParentBinding? Parent { get; init; }

    /// <summary>The attribute bindings in the order the binding writes them.</summary>
    public IReadOnlyList<KeyValuePair<string, AttributeBinding>> Attributes { get; init; } = [];

    public AttributeBinding? Source { get; init; }

    public AttributeBinding? Target { get; init; }

    public InsertSettings? Insert { get; init; }

    public RemoveSettings? Remove { get; init; }

    public bool SnapshotUndo { get; init; }

    public string? ReadOnly { get; init; }

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

public sealed record IdBinding(Slot? From, string? SidecarKey);

public sealed record ParentBinding(IReadOnlyList<string> Rules, string? Slot);

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

    /// <summary>A value computed by CEL, read-only.</summary>
    public sealed record Value(string Expression) : SlotKind
    {
        public override string ToString() => "value";
    }
}

/// <summary>An attribute binding (FBL §5.2): a slot with the options that decide reading and writing.</summary>
public sealed record AttributeBinding : Slot
{
    public EmptyRule Empty { get; init; } = EmptyRule.Keep;

    /// <summary>Per family name: what setting a value whose slot is absent does.</summary>
    public IReadOnlyDictionary<string, AbsentRule> Absent { get; init; } = new Dictionary<string, AbsentRule>();

    public JsonElement? Default { get; init; }

    /// <summary>Null for "shortest", else the number of decimals.</summary>
    public int? Decimals { get; init; }

    public bool KeepTimePrecision { get; init; }

    public WrittenStyle? Style { get; init; }

    public ReferenceBinding? Reference { get; init; }

    /// <summary>Wire value to model value, in document order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Map { get; init; }

    public Slot? Override { get; init; }

    public bool HtmlParagraphs { get; init; }

    public CreateChild? Create { get; init; }

    public string? ReadOnly { get; init; }

    /// <summary>yaml, json (FBL 0.5): what the slot reads from a scalar; null to let the attribute's type decide.</summary>
    public ReadAs? Read { get; init; }

    /// <summary>yaml, read as typed (FBL 0.5): whether a quoted scalar converts from its written text; null for not given, which does not.</summary>
    public QuotedRule? Quoted { get; init; }

    /// <summary>yaml, json (FBL 0.5): what the attribute is when its value does not convert, keeping the entry readable.</summary>
    public FallbackBinding? Fallback { get; init; }
}

public sealed record ReferenceBinding(IReadOnlyList<string> To, string By);

/// <summary>An attribute binding's <c>fallback</c> (FBL 0.5 §5.2): its value, and whether <c>fbl.unconverted-value</c> is reported.</summary>
public sealed record FallbackBinding(JsonElement Value, bool Report);

/// <summary>
/// How a missing child holding the value is written. xml: the element's text and its place. yaml
/// (FBL 0.4): <see cref="AtEnd"/>, each missing level of the mapping <c>child</c> reaches created at
/// the end of the level above it.
/// </summary>
public sealed record CreateChild(string Emit, ChildPlace Place, string? Before, bool AtEnd = false);

public sealed class InsertSettings
{
    public required InsertPlace Place { get; init; }

    public string? Container { get; init; }

    public CreateContainer? Create { get; init; }

    public IReadOnlyList<string> Keys { get; init; } = [];

    public string? Emit { get; init; }

    public string? Skeleton { get; init; }

    public string? When { get; init; }
}

/// <summary>How a missing container is created: where, with the key or selector <see cref="At"/> names, and its own text.</summary>
public sealed record CreateContainer(ContainerPlace At, string? Argument, string? Text);

/// <summary>
/// What goes with a removed entry. <see cref="RemoveEmptyLevels"/> (FBL 0.4) implies
/// <see cref="RemoveContainerWhenEmpty"/>, and removes the levels above the container that it leaves empty too.
/// </summary>
public sealed record RemoveSettings(IReadOnlyList<string> Cascade, bool RemoveContainerWhenEmpty, bool RemoveEmptyLevels = false);

public sealed class RegistrationSettings
{
    public IReadOnlyList<string> Headers { get; init; } = [];

    public bool CreateOnFirstPlacement { get; init; }

    public string? ResourceCapture { get; init; }

    public string? LegacyLayout { get; init; }
}

public sealed record TemplateSettings(string Text, IReadOnlyDictionary<string, string> ByOrigin);
