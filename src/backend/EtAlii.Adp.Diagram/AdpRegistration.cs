using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram;

/// <summary>An authored position in a registration's <c>layout:</c> block.</summary>
public readonly record struct AdpPosition(double X, double Y);

/// <summary>
/// An <c>.adp</c> registration file, read once (code-structure FR-061): the MIME line naming the
/// diagram type, the <c>body:</c> and <c>view:</c> headers below it, and the <c>layout:</c> block of
/// authored positions. Parsed by the rules the hierarchy project applied in five places, so a caller
/// moving onto it changes no answer; written back so that every byte the layout does not own stays
/// as it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>The headers</b> follow the MIME line. Up to eight lines after it are scanned, blanks skipped,
/// and the scan stops at the first line that is neither <c>body:</c> nor <c>view:</c>, so a file that
/// happens to continue with prose is not searched to its end.
/// </para>
/// <para>
/// <b>The layout block</b> is a <c>layout:</c> line followed by indented
/// <c>&lt;element-id&gt;: &lt;x&gt; &lt;y&gt;</c> entries, wherever in the file it stands. A malformed entry
/// is ignored rather than fatal - the block is metadata, and a hand-mangled line must never stop a
/// diagram from opening - and the block ends at the first line that is neither an entry nor blank.
/// </para>
/// <para>
/// <b><see cref="Read"/> is cached on the file's modification time and length</b>, so the router, the
/// hierarchy and an open session asking about one file read it once between writes.
/// </para>
/// </remarks>
public sealed class AdpRegistration
{
    private const string BodyHeader = "body:";
    private const string ViewHeader = "view:";
    private const string LayoutHeader = "layout:";

    /// <summary>How many lines after the MIME line are scanned for headers before giving up.</summary>
    private const int HeaderScanLimit = 8;

    private static readonly ConcurrentDictionary<string, Cached> _cache = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly IReadOnlyList<RawLine> _lines;

    private AdpRegistration(string text, IReadOnlyList<RawLine> lines, string? mimeType, string? body, string? view, IReadOnlyDictionary<string, AdpPosition> layout)
    {
        Text = text;
        _lines = lines;
        MimeType = mimeType;
        Body = body;
        View = view;
        Layout = layout;
    }

    /// <summary>The file's text, exactly as read.</summary>
    public string Text { get; }

    /// <summary>The first line, trimmed, or null for an empty file: the MIME type naming the diagram type.</summary>
    public string? MimeType { get; }

    /// <summary>The <c>body:</c> header - a document this registration opens but does not own - or null.</summary>
    public string? Body { get; }

    /// <summary>The <c>view:</c> header - which view within the body this registration opens - or null.</summary>
    public string? View { get; }

    /// <summary>The authored positions of the <c>layout:</c> block, empty when there is none.</summary>
    public IReadOnlyDictionary<string, AdpPosition> Layout { get; }

    /// <summary>The registration <paramref name="text"/> holds.</summary>
    public static AdpRegistration Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        (string? mimeType, string? body, string? view) = ReadHead(text);
        var lines = SplitLines(text);
        return new AdpRegistration(text, lines, mimeType, body, view, ReadLayout(lines));
    }

    /// <summary>
    /// The text of a new registration: <paramref name="mimeType"/> on the first line and, when the
    /// registration names a body it does not derive, a <c>body:</c> header naming
    /// <paramref name="bodyFileName"/>, each ended by <see cref="AdpFileWriter.NewLine"/>.
    /// </summary>
    public static string Compose(string mimeType, string? bodyFileName = null)
    {
        ArgumentNullException.ThrowIfNull(mimeType);

        var text = mimeType + AdpFileWriter.NewLine;
        return bodyFileName is null ? text : text + BodyHeader + " " + bodyFileName + AdpFileWriter.NewLine;
    }

    /// <summary>
    /// The registration at <paramref name="path"/>, or null when the file is not there or cannot be
    /// read. Answered from the cache while the file's modification time and length are unchanged.
    /// </summary>
    public static AdpRegistration? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        DateTime written;
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _cache.TryRemove(path, out _);
                return null;
            }

            written = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (_cache.TryGetValue(path, out var cached) && cached.Written == written && cached.Length == length)
        {
            return cached.Registration;
        }

        string text;
        try
        {
            // Shared, as every registration read has been: ADP's own layout write replaces this file
            // while the watcher's reload reads it, and a read sharing Read only would deny the replace.
            text = SharedDocumentReader.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // Stamped with what was seen before the read: a write landing in between leaves the newer
        // text under the older stamp, and the next read, seeing the newer stamp, parses again.
        var registration = Parse(text);
        _cache[path] = new Cached(written, length, registration);
        return registration;
    }

    /// <summary>
    /// The text with the layout block replaced by <paramref name="positions"/>, and every byte above
    /// and below it as it was. The block lands where it stood or, when there was none, directly after
    /// the header region - the MIME line and the contiguous <c>key: value</c> lines below it; an empty
    /// set removes the block. New lines take <see cref="AdpFileWriter.NewLine"/>; existing lines keep
    /// their own terminators.
    /// </summary>
    public string WithLayout(IReadOnlyDictionary<string, AdpPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        (int? blockStart, int blockEnd) = FindBlock(_lines);
        var insertAt = blockStart ?? EndOfHeaderRegion(_lines);

        var rebuilt = new StringBuilder();
        for (var index = 0; index < _lines.Count; index++)
        {
            // The emission runs BEFORE the old block is skipped: when the new block replaces an old
            // one in place, insertAt IS the old block's first line, and skipping first would drop the
            // block on every rewrite.
            if (index == insertAt)
            {
                AppendBlock(rebuilt, positions);
            }

            if (blockStart is not null && index >= blockStart && index < blockEnd)
            {
                continue;
            }

            rebuilt.Append(_lines[index].Content).Append(_lines[index].Terminator);
        }

        if (insertAt >= _lines.Count)
        {
            // The block belongs at the very end: a registration of headers only, or one whose
            // block was the last thing in it.
            AppendBlock(rebuilt, positions);
        }

        return rebuilt.ToString();
    }

    /// <summary>
    /// The text with a <c>body:</c> header whose final path segment is <paramref name="oldFileName"/>
    /// renamed to <paramref name="newFileName"/>, the rest of the header's relative path and every
    /// other byte left as they were; the text unchanged when no header in the header block names it.
    /// </summary>
    /// <remarks>
    /// The header block is the nine lines after the MIME line: blanks and <c>view:</c> headers are
    /// passed over, and the first <c>body:</c> header decides - renamed when it names the file, left
    /// alone otherwise. Any other line ends the block.
    /// </remarks>
    public string WithBodyFileName(string oldFileName, string newFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldFileName);
        ArgumentException.ThrowIfNullOrEmpty(newFileName);

        var lines = Text.Split('\n');
        for (var index = 1; index < lines.Length && index <= 9; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!trimmed.StartsWith(BodyHeader, StringComparison.OrdinalIgnoreCase))
            {
                if (!trimmed.StartsWith(ViewHeader, StringComparison.OrdinalIgnoreCase))
                {
                    break; // past the header block
                }

                continue;
            }

            var value = trimmed[BodyHeader.Length..].Trim();
            var segment = value.Replace('\\', '/');
            var lastSlash = segment.LastIndexOf('/');
            var fileSegment = lastSlash < 0 ? value : value[(lastSlash + 1)..];
            if (!string.Equals(fileSegment.TrimEnd('\r'), oldFileName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var prefixLength = line.Length - line.TrimStart().Length;
            var directoryPart = lastSlash < 0 ? "" : value[..(lastSlash + 1)];
            var carriage = line.EndsWith('\r') ? "\r" : "";
            lines[index] = line[..prefixLength] + BodyHeader + " " + directoryPart + newFileName + carriage;
            break;
        }

        return string.Join('\n', lines);
    }

    private static (string? MimeType, string? Body, string? View) ReadHead(string text)
    {
        using var reader = new StringReader(text);
        var mimeType = reader.ReadLine()?.Trim();

        string? body = null;
        string? view = null;
        for (var scanned = 0; scanned < HeaderScanLimit; scanned++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith(BodyHeader, StringComparison.OrdinalIgnoreCase))
            {
                body = Value(trimmed, BodyHeader);
            }
            else if (trimmed.StartsWith(ViewHeader, StringComparison.OrdinalIgnoreCase))
            {
                view = Value(trimmed, ViewHeader);
            }
            else
            {
                break;
            }
        }

        return (mimeType, body, view);

        static string? Value(string line, string header)
        {
            var value = line[header.Length..].Trim();
            return value.Length == 0 ? null : value;
        }
    }

    private static Dictionary<string, AdpPosition> ReadLayout(IReadOnlyList<RawLine> lines)
    {
        var positions = new Dictionary<string, AdpPosition>(StringComparer.Ordinal);
        var inBlock = false;
        foreach (var line in lines)
        {
            var content = line.Content;
            if (!inBlock)
            {
                inBlock = content.TrimEnd() == LayoutHeader;
                continue;
            }

            if (TryParseEntry(content, out var id, out var position))
            {
                positions[id] = position;
                continue;
            }

            if (content.Trim().Length == 0)
            {
                continue;
            }

            break;
        }

        return positions;
    }

    private static void AppendBlock(StringBuilder builder, IReadOnlyDictionary<string, AdpPosition> positions)
    {
        if (positions.Count == 0)
        {
            return;
        }

        builder.Append(LayoutHeader).Append(AdpFileWriter.NewLine);
        foreach ((string id, AdpPosition position) in positions.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder
                .Append("  ").Append(id).Append(": ")
                .Append(position.X.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(position.Y.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(AdpFileWriter.NewLine);
        }
    }

    /// <summary>The block's line range [start, end), or (null, 0) when there is none.</summary>
    private static (int? Start, int End) FindBlock(IReadOnlyList<RawLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Content.TrimEnd() != LayoutHeader)
            {
                continue;
            }

            var end = index + 1;
            while (end < lines.Count && (TryParseEntry(lines[end].Content, out _, out _) || lines[end].Content.Trim().Length == 0))
            {
                end++;
            }

            return (index, end);
        }

        return (null, 0);
    }

    private static int EndOfHeaderRegion(IReadOnlyList<RawLine> lines)
    {
        var index = Math.Min(1, lines.Count); // line 0 is the MIME line
        while (index < lines.Count && IsHeaderLine(lines[index].Content))
        {
            index++;
        }

        return index;
    }

    private static bool IsHeaderLine(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length == 0)
        {
            return true; // a blank between headers stays part of the header region
        }

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && !trimmed.StartsWith(LayoutHeader, StringComparison.Ordinal) && trimmed[..colon].All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    private static bool TryParseEntry(string content, out string elementId, out AdpPosition position)
    {
        elementId = "";
        position = default;

        // An entry is indented; the block header itself and any following section are not.
        if (content.Length == 0 || !char.IsWhiteSpace(content[0]))
        {
            return false;
        }

        var trimmed = content.Trim();
        var colon = trimmed.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        var parts = trimmed[(colon + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        elementId = trimmed[..colon];
        position = new AdpPosition(x, y);
        return elementId.Length > 0;
    }

    /// <summary>The text's lines with their own terminators, so a rebuild keeps every byte it does not mean to change.</summary>
    private static List<RawLine> SplitLines(string text)
    {
        var lines = new List<RawLine>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var hasCarriage = index > start && text[index - 1] == '\r';
            lines.Add(new RawLine(text[start..(index - (hasCarriage ? 1 : 0))], hasCarriage ? "\r\n" : "\n"));
            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new RawLine(text[start..], ""));
        }

        return lines;
    }

    private readonly record struct RawLine(string Content, string Terminator);

    private sealed record Cached(DateTime Written, long Length, AdpRegistration Registration);
}
