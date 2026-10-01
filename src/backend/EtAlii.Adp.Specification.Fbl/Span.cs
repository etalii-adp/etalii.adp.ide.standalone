namespace EtAlii.Adp.Specification.Fbl;

/// <summary>A range of UTF-8 byte offsets into a body, <see cref="End"/> exclusive.</summary>
public readonly record struct Span(int Start, int End)
{
    public int Length => End - Start;

    public bool Contains(Span other) => other.Start >= Start && other.End <= End;

    public override string ToString() => $"[{Start}, {End})";
}
