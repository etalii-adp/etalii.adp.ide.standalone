namespace EtAlii.Adp.Specification.Fbl;

/// <summary>The eleven splice operations of FBL §6.1. Every write is one of these and nothing else.</summary>
public enum SpliceOperation
{
    ReplaceValue,
    InsertKey,
    RemoveKey,
    InsertEntry,
    RemoveEntry,
    EnsureContainer,
    RemoveContainer,
    RewriteReference,
    ReEmitLine,
    OpenBlock,
    SelfClose,
}

/// <summary>The replacement of the bytes <see cref="Start"/> to <see cref="End"/> with <see cref="Text"/>.</summary>
public sealed record Splice(SpliceOperation Operation, int Start, int End, string Text)
{
    private static string NameOf(SpliceOperation operation) => operation switch
    {
        SpliceOperation.ReplaceValue => "replace-value",
        SpliceOperation.InsertKey => "insert-key",
        SpliceOperation.RemoveKey => "remove-key",
        SpliceOperation.InsertEntry => "insert-entry",
        SpliceOperation.RemoveEntry => "remove-entry",
        SpliceOperation.EnsureContainer => "ensure-container",
        SpliceOperation.RemoveContainer => "remove-container",
        SpliceOperation.RewriteReference => "rewrite-reference",
        SpliceOperation.ReEmitLine => "re-emit-line",
        SpliceOperation.OpenBlock => "open-block",
        SpliceOperation.SelfClose => "self-close",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    public static SpliceOperation Parse(string name) => name switch
    {
        "replace-value" => SpliceOperation.ReplaceValue,
        "insert-key" => SpliceOperation.InsertKey,
        "remove-key" => SpliceOperation.RemoveKey,
        "insert-entry" => SpliceOperation.InsertEntry,
        "remove-entry" => SpliceOperation.RemoveEntry,
        "ensure-container" => SpliceOperation.EnsureContainer,
        "remove-container" => SpliceOperation.RemoveContainer,
        "rewrite-reference" => SpliceOperation.RewriteReference,
        "re-emit-line" => SpliceOperation.ReEmitLine,
        "open-block" => SpliceOperation.OpenBlock,
        "self-close" => SpliceOperation.SelfClose,
        _ => throw new ArgumentException($"'{name}' is not an FBL splice operation.", nameof(name)),
    };

    public override string ToString() => $"{NameOf(Operation)} {Start}-{End} \"{Text}\"";
}

/// <summary>
/// One edit: the splices one model change (one DISL transaction) was planned as, applied together,
/// recorded together and undone together (FBL §6.4). Offsets refer to the body before the edit.
/// </summary>
public sealed record Edit(IReadOnlyList<Splice> Splices, bool Snapshot = false)
{
    public static Edit Empty { get; } = new([]);

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
