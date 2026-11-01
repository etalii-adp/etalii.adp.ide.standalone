namespace EtAlii.Adp.Specification.Fbl;

/// <summary>
/// An element or a relation read from a body (FBL §5). <see cref="IdIsStored"/> is false when the id
/// is not kept in the body: derived by the caller's DISL id strategy, or a place-based address.
/// </summary>
public sealed record FblElement(
    string Id,
    bool IdIsStored,
    string Type,
    string Rule,
    bool IsRelation,
    IReadOnlyDictionary<string, object?> Attributes,
    string? ParentId,
    string? ParentSlot,
    string? Source,
    string? Target,
    Span OwnSpan,
    int Line)
{
    /// <summary>
    /// The file the element is in, relative to a folder subject and <c>/</c>-separated, as the plugin
    /// that read it says (FBL §11.2); null for an element of a file body, whose file is the body's.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// Where each of its writable values is written, by attribute, beside <see cref="OwnSpan"/>: what a persistence
    /// plugin's <c>read</c> reports as the span of every writable value (FBL §11.2). A value without an entry has no
    /// span of its own, as one written once for several elements does not; empty when the reader reports none.
    /// </summary>
    public IReadOnlyDictionary<string, Span> ValueSpans { get; init; } = new Dictionary<string, Span>();

    /// <summary>The <c>sidecar.kind</c> of the element's rule, the key space its stored id is kept in (FBL 0.12 §8.6); null without one.</summary>
    public string? IdentityKind { get; init; }

    /// <summary>
    /// The key the element's stored id is kept under (FBL §5.3, 0.12 §8.6): its rule's <c>sidecar.key</c>, followed for a later
    /// holder of one key by the rule's <c>ordinal</c>; null for a rule without a sidecar id, or a key that does not evaluate.
    /// </summary>
    public string? IdentityKey { get; init; }
}

/// <summary>What reading a body gives: its elements and relations in document order, and its findings.</summary>
public sealed record FblModel(IReadOnlyList<FblElement> Elements, IReadOnlyList<Finding> Findings, bool Unreadable)
{
    public FblElement? Find(string id) => Elements.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// The views the body defines (FBL §4.7, FBL 0.11 §4.9), in document order, each with the block rule that matched it,
    /// so that a host whose tool types tell views apart by their kind reads the kind from the rule (a C4 view's).
    /// </summary>
    public IReadOnlyList<FblView> Views { get; init; } = [];

    /// <summary>The names of the views the body defines (FBL §4.7, FBL 0.11 §4.9), in document order.</summary>
    public IReadOnlyList<string> ViewNames => [.. Views.Select(view => view.Name)];

    /// <summary>
    /// The name of the view the reading shows (FBL §9.3, FBL 0.11 §4.9): the one the registration's <c>view</c> header names,
    /// matched ignoring case, or without it the first of the reading's origin; null when the body defines no view or the
    /// header names none. A legacy layout keeps its positions under this name (FBL §8.7).
    /// </summary>
    public string? SelectedView { get; init; }

    /// <summary>The resources a body holds (FBL §8.2): the values of the binding's resource capture, in document order.</summary>
    public IReadOnlyList<string> Resources { get; init; } = [];

    /// <summary>
    /// The view a registration's <c>view</c> header selects (FBL §9.3): the one of that name, ignoring
    /// case; without the header, the first in document order; null when there is none.
    /// </summary>
    internal FblView? SelectView(string? header) =>
        header is null ? Views.FirstOrDefault() : Views.FirstOrDefault(v => string.Equals(v.Name, header, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a registration's <c>view</c> header names no view of this body (FBL 0.11 §9.3): false without a header, and
    /// for a body that defines no views, whose reading shows the body as before.
    /// </summary>
    public bool ShowsNoView(string? header) => header is not null && Views.Count > 0 && SelectView(header) is null;
}

/// <summary>A view a block of the body defines (FBL §4.7): its name, the block rule that matched it, and where it is.</summary>
public sealed record FblView(string Name, string Block, Span Span, int Line);

/// <summary>What a DISL id strategy is asked when a rule stores no id (FBL §5.3, DISL §11.5).</summary>
public sealed record IdRequest(
    string Rule,
    string Type,
    IReadOnlyDictionary<string, object?> Attributes,
    string? Source,
    string? Target,
    int Line)
{
    /// <summary>The id of the element that contains this one (FBL §5.5), or null at the top level.</summary>
    /// <remarks>Known before this element's id is asked for: an element is read after the one that contains it.</remarks>
    public string? ParentId { get; init; }

    /// <summary>The containment slot of the parent this element is in, or null at the top level.</summary>
    public string? ParentSlot { get; init; }

    /// <summary>
    /// The element's zero-based place among the elements of the same parent and slot, in document
    /// order - what an id strategy that addresses an element by its position derives from.
    /// </summary>
    public int PositionInSlot { get; init; }
}

/// <summary>The caller's settings for reading and writing one body.</summary>
public sealed class FblOptions
{
    /// <summary>The body's file name, relative to the subject, for findings.</summary>
    public string FileName { get; init; } = "body";

    /// <summary>
    /// How long one expression may take on one statement before it is read as not matching. It bounds an
    /// expression that backtracks without end, and it is wall-clock: a thread paused beside a busy machine
    /// runs out of it on a match that takes microseconds. At 250 ms a `dotnet test` of the whole solution
    /// lost a line of a causal-loop example that way (2026-10-31), which reads as a wrong finding.
    /// </summary>
    public static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(2);

    public TimeSpan RegexTimeout { get; } = DefaultRegexTimeout;

    /// <summary>
    /// A body larger than this is unreadable rather than read in part (FBL §16); for a folder subject,
    /// a selected file larger than this is handed to the plugin by its path alone (<see cref="Plugins.PluginFile.IsOversized"/>).
    /// </summary>
    public int MaxBodyBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>A body with more entries than this is unreadable rather than read in part (FBL §16).</summary>
    public int MaxEntries { get; } = 500_000;

    /// <summary>
    /// The DISL id strategy for rules that store no id: returns the derived id, or null to address the
    /// element by its place. FBL defines no derivation of its own (FBL §5.3).
    /// </summary>
    public Func<IdRequest, string?>? DeriveId { get; init; }

    /// <summary>
    /// The type the tool type's specification gives an attribute of an element type, by the two
    /// names, or null where it gives none. A binding does not carry it, and without it every value
    /// reads as what its family makes of it; with it, an entry holding a value that does not
    /// convert is an unreadable entry: reported, no element, and never rewritten (FBL §7.4).
    /// </summary>
    public Func<string, string, string?>? AttributeType { get; init; }

    /// <summary>
    /// Whether the tool type's specification gives an attribute of an element type <c>many</c>, by the two names. A
    /// word slot of such an attribute holds every word of its group that matches, as a list (FBL 0.7 §4.6); without
    /// this, it holds the first. A binding does not carry it.
    /// </summary>
    public Func<string, string, bool>? Many { get; init; }

    /// <summary>
    /// The binding types the tool type's specification maps as unreadable (DESL §5.2): an entry a rule of such a type
    /// takes is an unreadable entry, reported and kept, and nothing nested in it is looked into (FBL 0.7 §4.8, §7.4).
    /// </summary>
    public IReadOnlySet<string> UnreadableTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The attributes whose type, in the tool type's specification, is a date or a date-time, each
    /// as <c>rule.attribute</c>. FBL does not know an attribute's type and section 6.3 needs it: a
    /// new value that reads as a timestamp is written plain when it IS one, and quoted when it is a
    /// string that only looks like one. Without this, every such value is taken for a string.
    /// </summary>
    public IReadOnlySet<string> TimeAttributes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether a key of a read entry that no rule of the binding reads is reported, as
    /// <see cref="FindingCodes.UnboundKey"/> of severity info. Such a key is kept on every write either
    /// way (FBL §6.1); this only says so, for a tool type whose specification asks that its author
    /// is told. Off unless asked for; in xml it is an attribute or a child element of a read element.
    /// </summary>
    public bool ReportUnboundKeys { get; init; }

    /// <summary>The registration's headers, visible to CEL as <c>registration</c>.</summary>
    public IReadOnlyDictionary<string, string> RegistrationHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>The registration's <c>resource</c> header (FBL §8.2): which value of the binding's resource capture is read.</summary>
    public string? Resource { get; init; }

    /// <summary>
    /// The registration's <c>view</c> header (FBL §9.3): which view of the body the reading shows, matched ignoring the case
    /// of ASCII letters; null for the first view in document order. A header that names no view shows none (FBL 0.11).
    /// </summary>
    public string? View { get; init; }

    /// <summary>
    /// The origin of the reading (FBL 0.10 §5.1, §9.1): the tool type the body is read as, which decides the rules it
    /// is read by and the resource capture it selects by (§8.2). Null reads by every rule, as before 0.10.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>
    /// For a folder subject, the entries the host adds beside the files the file rules select, each a
    /// path relative to the folder and handed to the plugin with no bytes (<see cref="Plugins.PluginReadRequest.SupplementaryEntries"/>);
    /// it is given the folder's full path. This is outside FBL 0.2, whose file rules select files only:
    /// it lets a host tell a plugin about entries, such as a directory, that no file rule can select,
    /// so that the plugin itself opens nothing.
    /// </summary>
    public Func<string, IReadOnlyList<string>>? SupplementaryEntries { get; init; }

    /// <summary>The registration's <c>identities</c> block, for rules with <c>id.sidecar</c>.</summary>
    public IReadOnlyDictionary<string, string> Identities { get; init; } = new Dictionary<string, string>();

    /// <summary>These options for the reading of another origin and resource of the same body (FBL 0.10 §9.1): everything else as it is.</summary>
    internal FblOptions With(string? origin, string? resource) => new()
    {
        FileName = FileName,
        MaxBodyBytes = MaxBodyBytes,
        DeriveId = DeriveId,
        AttributeType = AttributeType,
        Many = Many,
        UnreadableTypes = UnreadableTypes,
        TimeAttributes = TimeAttributes,
        ReportUnboundKeys = ReportUnboundKeys,
        RegistrationHeaders = RegistrationHeaders,
        Resource = resource,
        Origin = origin,
        View = View,
        SupplementaryEntries = SupplementaryEntries,
        Identities = Identities,
    };
}
