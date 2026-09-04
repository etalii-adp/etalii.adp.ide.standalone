using System.Text;

namespace EtAlii.Adp.Editor;

/// <summary>
/// A text file the whole editor family can hold: UTF-8 (with or without BOM) or pure ASCII,
/// each line keeping its OWN terminator so an unchanged file saves back byte-identical -
/// mixed line endings included. Anything else is refused with a reason, never guessed at:
/// a file over the size limit before it is even read, a binary file, a non-UTF-8 encoding
/// (modular-text-editors Requirements 6.1, 6.3, 6.6, 7.1, 7.2).
/// </summary>
/// <remarks>
/// The line-preservation idea deliberately duplicates the shape of the C4 module's own
/// buffer rather than sharing it - consolidating two independently-written buffers is a
/// future cleanup spec's decision, recorded in this spec's design, and this file touches
/// nothing under <c>src/diagrams/</c>.
/// </remarks>
public sealed class TextFileBuffer
{
    /// <summary>Requirement 6.6's limit, a design-time bound for a hand-edited text file.</summary>
    public const long SizeLimitInBytes = 5 * 1024 * 1024;

    /// <summary>How much of the file the binary heuristic inspects.</summary>
    private const int BinaryProbeLength = 8 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _path;
    private readonly bool _hasBom;
    private readonly string[] _terminators;
    private readonly string _dominantTerminator;

    private TextFileBuffer(string path, bool hasBom, string content, string[] terminators, string dominantTerminator)
    {
        _path = path;
        _hasBom = hasBom;
        Content = content;
        _terminators = terminators;
        _dominantTerminator = dominantTerminator;
    }

    /// <summary>The file's text exactly as read - original terminators intact, BOM excluded.</summary>
    public string Content { get; private set; }

    /// <summary>
    /// The detected encoding, as the property grid words it (modular-text-editors Requirement
    /// 9.1). Detection is scoped to UTF-8 with/without BOM, ASCII included by construction, so
    /// this names one of exactly two answers.
    /// </summary>
    public string EncodingName => _hasBom ? "UTF-8 with BOM" : "UTF-8";

    /// <summary>
    /// The detected line-ending style: the one style when the file is consistent, or the
    /// dominant one named honestly as mixed - a fact the save preserves per line either way.
    /// </summary>
    public string LineEndingStyle
    {
        get
        {
            var used = _terminators.Where(terminator => terminator.Length > 0).Distinct().ToArray();
            return used.Length switch
            {
                0 => NameOf(_dominantTerminator),
                1 => NameOf(used[0]),
                _ => $"Mixed (mostly {NameOf(_dominantTerminator)})",
            };

            static string NameOf(string terminator) => terminator == "\r\n" ? "CRLF" : "LF";
        }
    }

    /// <summary>How many lines the file has, the way an editor's gutter counts them.</summary>
    public int LineCount => _terminators.Length;

    /// <summary>
    /// Opens <paramref name="path"/>, or refuses with a reason a user can act on. The size
    /// check runs before any byte is read (Requirement 6.6).
    /// </summary>
    public static TextFileBufferOpenResult Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return TextFileBufferOpenResult.Refused($"'{Path.GetFileName(path)}' does not exist.");
            }

            if (info.Length > SizeLimitInBytes)
            {
                return TextFileBufferOpenResult.Refused(
                    $"'{Path.GetFileName(path)}' is {info.Length / (1024 * 1024)} MB, over the {SizeLimitInBytes / (1024 * 1024)} MB limit for text editing.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TextFileBufferOpenResult.Refused($"'{Path.GetFileName(path)}' could not be read: {exception.Message}");
        }

        byte[] bytes;
        try
        {
            // Shared exactly as SharedDocumentReader shares, and for its reason: a save landing
            // while this read is in flight must not fail, and ADP's own temp-then-move publish
            // must be able to replace the file underneath it. Sharing and decoding strictness
            // are orthogonal - what arrives is still decoded strictly below, and a torn read is
            // still refused rather than saved back (Requirement 2.4).
            //
            // It opens its own stream rather than calling SharedDocumentReader because that type
            // lives in EtAlii.Adp.Backend, which REFERENCES this project: the helper sits above
            // its caller, so the call cannot be made without inverting that dependency. These
            // flags are not an unguarded second copy of the rule - ShapeOfFileAccess asserts
            // this exact combination on every production file, including this one.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.SequentialScan);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TextFileBufferOpenResult.Refused($"'{Path.GetFileName(path)}' could not be read: {exception.Message}");
        }

        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = hasBom ? bytes.AsSpan(3) : bytes.AsSpan();

        if (LooksBinary(body))
        {
            // Requirement 7.2: say what would work instead, where something would.
            return TextFileBufferOpenResult.Refused(
                $"'{Path.GetFileName(path)}' looks like a binary file, which no text editor here can open. If it is a diagram body, open it through its diagram instead.");
        }

        string content;
        try
        {
            // Strict: an invalid sequence is a refusal, not a replacement character - the file
            // saved back would otherwise silently differ from the file opened.
            content = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return TextFileBufferOpenResult.Refused(
                $"'{Path.GetFileName(path)}' is not UTF-8 or ASCII, the encodings text editing supports. Convert it to UTF-8 to edit it here.");
        }

        var (terminators, dominant) = TerminatorsOf(content);
        return TextFileBufferOpenResult.Opened(new TextFileBuffer(path, hasBom, content, terminators, dominant));
    }

    /// <summary>
    /// Writes <paramref name="newContent"/> back: every line that already existed keeps its
    /// own terminator, new lines get the file's dominant style, and the BOM comes back iff
    /// the file arrived with one. Returns an empty string, or the reason the save failed.
    /// </summary>
    public async Task<string> SaveAsync(string newContent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newContent);

        var rendered = Render(newContent);
        var bytes = StrictUtf8.GetBytes(rendered);
        try
        {
            await using var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
            if (_hasBom)
            {
                await stream.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF }, cancellationToken);
            }

            await stream.WriteAsync(bytes, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{Path.GetFileName(_path)}' could not be saved: {exception.Message}";
        }

        Content = rendered;
        return "";
    }

    /// <summary>
    /// The new content with terminators re-applied: lines are read newline-tolerantly
    /// (<c>\r\n</c> or <c>\n</c>), then line <c>i</c> gets the original line <c>i</c>'s
    /// terminator while it exists and the dominant style after that. Unchanged content
    /// renders byte-identically by construction.
    /// </summary>
    private string Render(string newContent)
    {
        var lines = SplitLines(newContent);
        var builder = new StringBuilder(newContent.Length + 16);
        for (var index = 0; index < lines.Count; index++)
        {
            builder.Append(lines[index].Text);
            var isLast = index == lines.Count - 1;
            var original = index < _terminators.Length ? _terminators[index] : _dominantTerminator;
            builder.Append(isLast && lines[index].Terminator.Length == 0
                // The final line had no newline of its own in the incoming text: it keeps the
                // original's decision - a file that never ended in a newline still does not.
                ? (index < _terminators.Length ? original : "")
                : original.Length > 0 ? original : _dominantTerminator);
        }

        return builder.ToString();
    }

    private static (string[] Terminators, string Dominant) TerminatorsOf(string content)
    {
        var lines = SplitLines(content);
        var terminators = lines.Select(line => line.Terminator).ToArray();
        var crlf = terminators.Count(terminator => terminator == "\r\n");
        var lf = terminators.Count(terminator => terminator == "\n");
        return (terminators, crlf > lf ? "\r\n" : "\n");
    }

    private static List<(string Text, string Terminator)> SplitLines(string content)
    {
        var lines = new List<(string, string)>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\n')
            {
                continue;
            }

            var hasCarriage = index > 0 && content[index - 1] == '\r';
            var textEnd = hasCarriage ? index - 1 : index;
            lines.Add((content[start..textEnd], hasCarriage ? "\r\n" : "\n"));
            start = index + 1;
        }

        if (start < content.Length || lines.Count == 0)
        {
            lines.Add((content[start..], ""));
        }

        return lines;
    }

    /// <summary>A null byte, or a majority of non-printable bytes in the probe, reads as binary (Requirement 7.1).</summary>
    private static bool LooksBinary(ReadOnlySpan<byte> body)
    {
        var probe = body.Length > BinaryProbeLength ? body[..BinaryProbeLength] : body;
        if (probe.Length == 0)
        {
            return false;
        }

        var suspicious = 0;
        foreach (var value in probe)
        {
            if (value == 0)
            {
                return true;
            }

            if (value < 0x09 || (value > 0x0D && value < 0x20))
            {
                suspicious++;
            }
        }

        return suspicious * 2 > probe.Length;
    }
}
