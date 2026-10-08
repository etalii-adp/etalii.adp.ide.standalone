using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>The splices of one edit while it is being planned, against the body before the edit.</summary>
internal sealed class Plan(BodyReading reading)
{
    private readonly List<Splice> _splices = [];

    public BodyReading Reading { get; } = reading;

    public bool Snapshot { get; set; }

    public void Add(SpliceOperation operation, int start, int end, string text)
    {
        if (start == end && text.Length == 0) return;
        _splices.Add(new Splice(operation, start, end, text));
    }

    public void Add(SpliceOperation operation, Span span, string text) => Add(operation, span.Start, span.End, text);

    /// <summary>Whether a splice already replaces bytes overlapping <paramref name="span"/>.</summary>
    public bool Touches(Span span) => _splices.Any(s => s.Start < span.End && span.Start < s.End);

    public static void Refuse(string reason) => throw new RefusedException(reason);

    /// <summary>
    /// The splices in body order: sorted by start, those at one offset kept in the order they were
    /// planned (FBL §6.5). Overlapping splices are a planning error.
    /// </summary>
    public IReadOnlyList<Splice> Ordered()
    {
        var ordered = _splices.Select((s, i) => (s, i)).OrderBy(p => p.s.Start).ThenBy(p => p.i).Select(p => p.s).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
            {
                throw new InvalidOperationException($"Two splices of one edit overlap: {ordered[i - 1]} and {ordered[i]}.");
            }
        }
        return ordered;
    }
}
