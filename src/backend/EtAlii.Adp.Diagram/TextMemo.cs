namespace EtAlii.Adp.Diagram;

/// <summary>
/// What was last read from a body's text, kept with the bytes it was read from: asked again for the
/// same bytes it reads nothing, and an edit makes it stale by itself, because an edit gives the body
/// new bytes.
/// </summary>
/// <remarks>
/// The bytes are compared by reference, not by content: a body's bytes are never changed in place,
/// so the same array is the same text, and the comparison costs nothing however large the text is.
/// </remarks>
/// <typeparam name="T">What is read: the binding's reading, the DISL model built from it, the lines.</typeparam>
public sealed class TextMemo<T>
{
    private (byte[] Bytes, T Value)? _last;

    /// <summary>What <paramref name="read"/> gives for <paramref name="bytes"/>, read only when these bytes have not been read before.</summary>
    public T Of(byte[] bytes, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(read);

        if (_last is not { } last || !ReferenceEquals(last.Bytes, bytes))
        {
            _last = last = (bytes, read());
        }

        return last.Value;
    }
}
