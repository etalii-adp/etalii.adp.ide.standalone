namespace EtAlii.Adp.Editor;

/// <summary>
/// What opening a file produced: the buffer, or the reason it was refused - a size over the
/// limit, binary content, a non-UTF-8 encoding. Refusal is a value, not an exception, because
/// every one of these is an ordinary per-file answer the user acts on, not a fault.
/// </summary>
public sealed record TextFileBufferOpenResult(TextFileBuffer? Buffer, string Refusal)
{
    public static TextFileBufferOpenResult Opened(TextFileBuffer buffer) => new(buffer, "");

    public static TextFileBufferOpenResult Refused(string reason) => new(null, reason);
}
