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
    int Line);

/// <summary>What reading a body gives: its elements and relations in document order, and its findings.</summary>
public sealed record FblModel(IReadOnlyList<FblElement> Elements, IReadOnlyList<Finding> Findings, bool Unreadable)
{
    public static FblModel Empty { get; } = new([], [], false);

    public IEnumerable<FblElement> Nodes => Elements.Where(e => !e.IsRelation);

    public IEnumerable<FblElement> Relations => Elements.Where(e => e.IsRelation);

    public FblElement? Find(string id) => Elements.FirstOrDefault(e => e.Id == id);

    /// <summary>The views a blocks body defines (FBL §4.7), in document order.</summary>
    public IReadOnlyList<FblView> Views { get; init; } = [];

    /// <summary>The resources a body holds (FBL §8.2): the values of the binding's resource capture, in document order.</summary>
    public IReadOnlyList<string> Resources { get; init; } = [];

    /// <summary>
    /// The view a registration's <c>view</c> header selects (FBL §9.3): the one of that name, ignoring
    /// case; without the header, the first in document order; null when there is none.
    /// </summary>
    public FblView? SelectView(string? header) =>
        header is null ? Views.FirstOrDefault() : Views.FirstOrDefault(v => string.Equals(v.Name, header, StringComparison.OrdinalIgnoreCase));
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
    int Line);

/// <summary>The caller's settings for reading and writing one body.</summary>
public sealed class FblOptions
{
    /// <summary>The body's file name, relative to the subject, for findings.</summary>
    public string FileName { get; init; } = "body";

    public TimeSpan RegexTimeout { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>A body larger than this is unreadable rather than read in part (FBL §16).</summary>
    public int MaxBodyBytes { get; } = 32 * 1024 * 1024;

    /// <summary>A body with more entries than this is unreadable rather than read in part (FBL §16).</summary>
    public int MaxEntries { get; } = 500_000;

    /// <summary>
    /// The DISL id strategy for rules that store no id: returns the derived id, or null to address the
    /// element by its place. FBL defines no derivation of its own (FBL §5.3).
    /// </summary>
    public Func<IdRequest, string?>? DeriveId { get; init; }

    /// <summary>The registration's headers, visible to CEL as <c>registration</c>.</summary>
    public IReadOnlyDictionary<string, string> RegistrationHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>The registration's <c>resource</c> header (FBL §8.2): which value of the binding's resource capture is read.</summary>
    public string? Resource { get; init; }

    /// <summary>The registration's <c>identities</c> block, for rules with <c>id.sidecar</c>.</summary>
    public IReadOnlyDictionary<string, string> Identities { get; init; } = new Dictionary<string, string>();
}
