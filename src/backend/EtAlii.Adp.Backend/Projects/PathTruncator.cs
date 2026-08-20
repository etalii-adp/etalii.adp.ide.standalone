using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Projects;

/// <summary>
/// Middle-ellipsis truncation for display purposes, adapted from the classic
/// "split the remaining budget between a head and a tail" approach (see
/// https://codereview.stackexchange.com/questions/289368) but operating on whole
/// path segments first, so a folder name is never cut in half - only whole
/// segments are dropped from the middle. Falls back to character-level
/// truncation when there aren't enough segments to drop from (a bare path or a
/// single very long segment).
/// </summary>
public static class PathTruncator
{
    public const int DefaultMaxLength = 40;
    private const string Ellipsis = "...";

    public static string Truncate(IReadOnlyList<string> segments, int maxLength = DefaultMaxLength)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var separator = IoPath.DirectorySeparatorChar.ToString();
        var full = string.Join(separator, segments);

        if (full.Length <= maxLength)
        {
            return full;
        }

        if (segments.Count <= 2)
        {
            return TruncateMiddle(full, maxLength);
        }

        var frontCount = 1;
        var backCount = 1;

        while (frontCount + backCount < segments.Count)
        {
            if (CombinedLength(segments, frontCount + 1, backCount, separator) <= maxLength)
            {
                frontCount++;
            }
            else if (CombinedLength(segments, frontCount, backCount + 1, separator) <= maxLength)
            {
                backCount++;
            }
            else
            {
                break;
            }
        }

        var result = string.Join(separator, segments.Take(frontCount))
            + separator + Ellipsis + separator
            + string.Join(separator, segments.Skip(segments.Count - backCount));

        return result.Length <= maxLength ? result : TruncateMiddle(full, maxLength);
    }

    private static int CombinedLength(IReadOnlyList<string> segments, int frontCount, int backCount, string separator)
    {
        var frontLength = segments.Take(frontCount).Sum(s => s.Length) + separator.Length * Math.Max(0, frontCount - 1);
        var backLength = segments.Skip(segments.Count - backCount).Sum(s => s.Length) + separator.Length * Math.Max(0, backCount - 1);
        return frontLength + separator.Length + Ellipsis.Length + separator.Length + backLength;
    }

    private static string TruncateMiddle(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= Ellipsis.Length)
        {
            return Ellipsis[..Math.Max(0, maxLength)];
        }

        var charsToKeep = maxLength - Ellipsis.Length;
        var headLength = (charsToKeep + 1) / 2;
        var tailLength = charsToKeep / 2;

        return text[..headLength] + Ellipsis + text[^tailLength..];
    }
}
