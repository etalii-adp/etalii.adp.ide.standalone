namespace EtAlii.Adp.Specification.Fbl;

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
