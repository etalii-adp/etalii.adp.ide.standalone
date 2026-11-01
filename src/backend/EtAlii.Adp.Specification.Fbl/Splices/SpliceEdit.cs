namespace EtAlii.Adp.Specification.Fbl;

/// <summary>
/// One edit: the splices one model change (one DISL transaction) was planned as, applied together,
/// recorded together and undone together (FBL §6.4). Offsets refer to the body before the edit.
/// </summary>
public sealed record SpliceEdit(IReadOnlyList<Splice> Splices, bool Snapshot = false)
{
    public static SpliceEdit Empty { get; } = new([]);

    /// <summary>Applies the splices to <paramref name="bytes"/>, in order, offsets referring to the bytes before.</summary>
    public static byte[] Apply(byte[] bytes, IReadOnlyList<Splice> splices)
    {
        using var output = new MemoryStream(bytes.Length + 64);
        var position = 0;
        foreach (var splice in splices)
        {
            if (splice.Start < position || splice.End < splice.Start || splice.End > bytes.Length)
            {
                throw new InvalidOperationException($"The splices of an edit overlap or leave the body: {splice} after offset {position}.");
            }
            output.Write(bytes, position, splice.Start - position);
            var text = System.Text.Encoding.UTF8.GetBytes(splice.Text);
            output.Write(text, 0, text.Length);
            position = splice.End;
        }
        output.Write(bytes, position, bytes.Length - position);
        return output.ToArray();
    }

    /// <summary>
    /// The splices that undo <paramref name="splices"/> once applied to <paramref name="before"/>: each
    /// puts its replaced bytes back, at its offset in the body after the edit, under its own operation
    /// (FBL §15.3).
    /// </summary>
    public static IReadOnlyList<Splice> Inverse(byte[] before, IReadOnlyList<Splice> splices)
    {
        var inverse = new List<Splice>(splices.Count);
        var shift = 0;
        foreach (var splice in splices)
        {
            var start = splice.Start + shift;
            var written = System.Text.Encoding.UTF8.GetByteCount(splice.Text);
            var replaced = System.Text.Encoding.UTF8.GetString(before, splice.Start, splice.End - splice.Start);
            inverse.Add(new Splice(splice.Operation, start, start + written, replaced));
            shift += written - (splice.End - splice.Start);
        }
        return inverse;
    }
}
