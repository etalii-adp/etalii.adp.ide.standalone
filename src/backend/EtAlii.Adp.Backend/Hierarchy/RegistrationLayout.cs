using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>An authored position stored in a registration's layout block.</summary>
public readonly record struct RegistrationPosition(double X, double Y);

/// <summary>
/// The <c>layout:</c> block of an <c>.adp</c> registration file: authored element positions,
/// stored as metadata enriching a body whose own format carries no layout (tech.md's
/// layout-in-.adp rule, databricks-diagrams Requirement 7).
/// </summary>
/// <remarks>
/// The block sits after the MIME line and the headers (<c>body:</c>, <c>view:</c> and their
/// kin): a <c>layout:</c> line followed by indented <c>&lt;element-id&gt;: &lt;x&gt; &lt;y&gt;</c>
/// entries. The <c>.adp</c> is ADP's own file, so the block itself may be normalized freely
/// on write - but everything above it is preserved byte for byte, because the headers belong
/// to <see cref="DiagramFilePair"/>'s contract and a layout write must never disturb them.
/// Defined once here, in core, and offered to every module; no module-specific knowledge
/// lives in this class.
/// </remarks>
public static class RegistrationLayout
{
    private const string BlockHeader = "layout:";

    /// <summary>
    /// The stored positions, empty when the file or the block is absent. Malformed entries
    /// are ignored rather than fatal: the block is metadata, and a hand-mangled line must
    /// never stop a diagram from opening.
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Read(string adpPath)
    {
        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var lines = ReadLines(adpPath);
        if (lines is null)
        {
            return positions;
        }

        var inBlock = false;
        foreach (var line in lines)
        {
            var content = line.Content;
            if (!inBlock)
            {
                inBlock = content.TrimEnd() == BlockHeader;
                continue;
            }

            if (TryParseEntry(content, out var id, out var position))
            {
                positions[id] = position;
                continue;
            }

            if (content.Trim().Length == 0)
            {
                continue; // a blank inside the block is tolerated
            }

            break; // the block ends at the first line that is not an entry
        }

        return positions;
    }

    /// <summary>
    /// Stored positions overlaid on computed ones, element by element: an element with a
    /// stored position takes it, every other keeps its computed place - so a config change
    /// adding elements degrades gracefully, and stale stored ids simply have nothing to
    /// override (Requirement 7.4/7.5).
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Apply(
        IReadOnlyDictionary<string, RegistrationPosition> computed,
        IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(stored);

        var merged = new Dictionary<string, RegistrationPosition>(computed, StringComparer.Ordinal);
        foreach (var (id, position) in stored)
        {
            if (merged.ContainsKey(id))
            {
                merged[id] = position;
            }
        }

        return merged;
    }

    /// <summary>
    /// Writes one element's position and answers with the entry it replaced - null when the
    /// element had none - which is exactly what an undoable command needs for its inverse.
    /// </summary>
    public static RegistrationPosition? SetPosition(string adpPath, string elementId, RegistrationPosition position)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var positions = new Dictionary<string, RegistrationPosition>(Read(adpPath), StringComparer.Ordinal);
        positions.TryGetValue(elementId, out var existing);
        var had = positions.ContainsKey(elementId);
        positions[elementId] = position;
        WriteBlock(adpPath, positions);
        return had ? existing : null;
    }

    /// <summary>Removes one element's entry; removing the last entry removes the block itself.</summary>
    public static void RemovePosition(string adpPath, string elementId)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var positions = new Dictionary<string, RegistrationPosition>(Read(adpPath), StringComparer.Ordinal);
        if (positions.Remove(elementId))
        {
            WriteBlock(adpPath, positions);
        }
    }

    /// <summary>
    /// Drops every stored id that is not in <paramref name="liveElementIds"/> - the "dropped
    /// on the next write" half of Requirement 7.5, for a module that knows which elements
    /// its document still holds.
    /// </summary>
    public static void Prune(string adpPath, IReadOnlySet<string> liveElementIds)
    {
        ArgumentNullException.ThrowIfNull(liveElementIds);

        var positions = Read(adpPath);
        var kept = positions
            .Where(entry => liveElementIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if (kept.Count != positions.Count)
        {
            WriteBlock(adpPath, kept);
        }
    }

    /// <summary>
    /// Replaces the layout block, leaving every byte above and below it untouched. The block
    /// lands where it was, or - when the file had none - directly after the header region:
    /// the MIME line and the contiguous <c>key: value</c> headers that follow it.
    /// </summary>
    private static void WriteBlock(string adpPath, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        var lines = ReadLines(adpPath) ?? throw new FileNotFoundException("The registration file is not there.", adpPath);

        var (blockStart, blockEnd) = FindBlock(lines);
        var insertAt = blockStart ?? EndOfHeaderRegion(lines);

        var rebuilt = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            // The emission check runs BEFORE the old-block skip: when the new block replaces
            // an old one in place, insertAt IS the old block's first line, and skipping first
            // would silently drop the block on every rewrite.
            if (index == insertAt)
            {
                AppendBlock(rebuilt, positions);
            }

            if (blockStart is not null && index >= blockStart && index < blockEnd)
            {
                continue; // the old block's lines; the new block was just emitted in their place
            }

            rebuilt.Append(lines[index].Content).Append(lines[index].Terminator);
        }

        if (insertAt >= lines.Count)
        {
            // The block belongs at the very end - a registration of headers only, or one
            // whose block was the last thing in the file.
            AppendBlock(rebuilt, positions);
        }

        File.WriteAllText(adpPath, rebuilt.ToString());
    }

    private static void AppendBlock(StringBuilder builder, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        if (positions.Count == 0)
        {
            return; // no entries, no block: the empty block is removed rather than kept
        }

        builder.Append(BlockHeader).Append("\r\n");
        foreach (var (id, position) in positions.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder
                .Append("  ").Append(id).Append(": ")
                .Append(position.X.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(position.Y.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("\r\n");
        }
    }

    /// <summary>The block's line range [start, end), or (null, 0) when the file has none.</summary>
    private static (int? Start, int End) FindBlock(IReadOnlyList<RawLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Content.TrimEnd() != BlockHeader)
            {
                continue;
            }

            var end = index + 1;
            while (end < lines.Count
                && (TryParseEntry(lines[end].Content, out _, out _) || lines[end].Content.Trim().Length == 0))
            {
                end++;
            }

            return (index, end);
        }

        return (null, 0);
    }

    /// <summary>
    /// Where a fresh block goes: after the MIME line and the contiguous run of headers
    /// (<c>key: value</c> lines) that follow it, before anything else the file may hold.
    /// </summary>
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
        return colon > 0 && !trimmed.StartsWith(BlockHeader, StringComparison.Ordinal) && trimmed[..colon].All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    private static bool TryParseEntry(string content, out string elementId, out RegistrationPosition position)
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
        position = new RegistrationPosition(x, y);
        return elementId.Length > 0;
    }

    private readonly record struct RawLine(string Content, string Terminator);

    /// <summary>The file's lines with their own terminators kept, so a rebuild preserves every byte it does not mean to change.</summary>
    private static List<RawLine>? ReadLines(string adpPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(adpPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var lines = new List<RawLine>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                var hasCarriage = index > start && text[index - 1] == '\r';
                var content = text[start..(index - (hasCarriage ? 1 : 0))];
                lines.Add(new RawLine(content, hasCarriage ? "\r\n" : "\n"));
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(new RawLine(text[start..], ""));
        }

        return lines;
    }
}
