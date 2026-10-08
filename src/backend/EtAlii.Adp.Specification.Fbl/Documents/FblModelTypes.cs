namespace EtAlii.Adp.Specification.Fbl.Documents;

using System.Text.Json;

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
}

public sealed record FileRule(string? Name, string Glob, Family? Family);

public sealed record PluginReader(string Plugin, string? Version);

public sealed class TextDefaults
{
    public string Newline { get; init; } = "\n";

    /// <summary>Spaces per step, or 0 for a tab.</summary>
    public int Indent { get; init; } = 2;

    public bool SequenceFlush { get; init; }

    public string Quote { get; init; } = "double";
}

public sealed record HeaderSettings(string? Key, JsonElement? Value, string? Line, bool Required);

public sealed record BlockRule(string Name, string Line, IReadOnlyList<string>? Within, bool CaseInsensitive, string? View);

/// <summary>An element rule or a relation rule (FBL §5.1, §5.4).</summary>
public sealed class Rule
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

    public AttributeBinding? Attribute(string name) => Attributes.FirstOrDefault(a => a.Key == name).Value;
}

public sealed record IdBinding(Slot? From, string? SidecarKey);

public sealed record ParentBinding(IReadOnlyList<string> Rules, string? Slot);

/// <summary>
/// A slot (FBL §5.2): exactly one place a value is read from and written to. Exactly one of the
/// slot kinds is set; <see cref="Child"/>, <see cref="Word"/> and <see cref="Flag"/> qualify it.
/// </summary>
public record Slot
{
    public string? Key { get; init; }

    public string? XmlAttribute { get; init; }

    public bool Text { get; init; }

    public string? Child { get; init; }

    public string? Group { get; init; }

    public string? Parent { get; init; }

    public string? Capture { get; init; }

    public string? Value { get; init; }

    public string? Word { get; init; }

    public bool Flag { get; init; }

    public bool IsComputed => Value is not null;

    public override string ToString() =>
        Key is not null ? $"key {Key}" :
        XmlAttribute is not null ? $"attribute {XmlAttribute}" :
        Text ? "text" :
        Group is not null ? $"group {Group}" :
        Parent is not null ? $"parent {Parent}" :
        Capture is not null ? $"capture {Capture}" :
        Value is not null ? "value" : "slot";
}

/// <summary>An attribute binding (FBL §5.2): a slot with the options that decide reading and writing.</summary>
public sealed record AttributeBinding : Slot
{
    /// <summary>"remove", "keep" or "refuse"; null for the default.</summary>
    public string? Empty { get; init; }

    /// <summary>Per family name: "insert" or "refuse".</summary>
    public IReadOnlyDictionary<string, string> Absent { get; init; } = new Dictionary<string, string>();

    public JsonElement? Default { get; init; }

    /// <summary>Null for "shortest", else the number of decimals.</summary>
    public int? Decimals { get; init; }

    public bool KeepTimePrecision { get; init; }

    public string? Style { get; init; }

    public ReferenceBinding? Reference { get; init; }

    /// <summary>Wire value to model value, in document order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Map { get; init; }

    public Slot? Override { get; init; }

    public bool HtmlParagraphs { get; init; }

    public CreateChild? Create { get; init; }

    public string? ReadOnly { get; init; }
}

public sealed record ReferenceBinding(IReadOnlyList<string> To, string By);

/// <summary>xml: how a missing child element holding the value is written.</summary>
public sealed record CreateChild(string Emit, string Place, string? Before);

public sealed class InsertSettings
{
    /// <summary>"after-last", "end", "start", "last-child", "next-sibling", "end-of-document" or "before".</summary>
    public required string Place { get; init; }

    public string? PlaceBefore { get; init; }

    public string? Container { get; init; }

    public CreateContainer? Create { get; init; }

    public IReadOnlyList<string> Keys { get; init; } = [];

    public string? Emit { get; init; }

    public string? Skeleton { get; init; }

    public string? When { get; init; }
}

/// <summary>How a missing container is created: <see cref="At"/> is "end-of-document", "before", "after" or "under".</summary>
public sealed record CreateContainer(string At, string? Argument, string? Text);

public sealed record RemoveSettings(IReadOnlyList<string> Cascade, bool RemoveContainerWhenEmpty);

public sealed class RegistrationSettings
{
    public IReadOnlyList<string> Headers { get; init; } = [];

    public bool CreateOnFirstPlacement { get; init; }

    public string? ResourceCapture { get; init; }

    public string? LegacyLayout { get; init; }
}

public sealed record TemplateSettings(string Text, IReadOnlyDictionary<string, string> ByOrigin);
