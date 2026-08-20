using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Projects;

/// <summary>
/// Middle-ellipsis truncation for display purposes, adapted from the classic
/// "split the remaining budget between a head and a tail" approach (see
/// https://codereview.stackexchange.com/questions/289368) but operating on whole
/// path segments first, so a folder name is never cut in half - only whole
/// segments are dropped from the middle. Among every front/back split that fits
/// maxLength, the one keeping the most segments is preferred, and ties are broken
/// by whichever keeps the head and tail closest in length - i.e. the ellipsis as
/// close to the middle of the result as possible. Falls back to character-level
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

        var split = FindMostCenteredSplit(segments, maxLength, separator);
        if (split is not var (frontCount, backCount))
        {
            return TruncateMiddle(full, maxLength);
        }

        return string.Join(separator, segments.Take(frontCount))
            + separator + Ellipsis + separator
            + string.Join(separator, segments.Skip(segments.Count - backCount));
    }

    private static (int FrontCount, int BackCount)? FindMostCenteredSplit(IReadOnlyList<string> segments, int maxLength, string separator)
    {
        (int FrontCount, int BackCount)? best = null;
        var bestTotal = 0;
        var bestImbalance = int.MaxValue;

        for (var frontCount = 1; frontCount < segments.Count; frontCount++)
        {
            var frontLength = SegmentsLength(segments, 0, frontCount, separator);
            var maxBackCount = segments.Count - frontCount - 1;

            for (var backCount = 1; backCount <= maxBackCount; backCount++)
            {
                var backLength = SegmentsLength(segments, segments.Count - backCount, backCount, separator);
                var combinedLength = frontLength + separator.Length + Ellipsis.Length + separator.Length + backLength;
                if (combinedLength > maxLength)
                {
                    continue;
                }

                var total = frontCount + backCount;
                var imbalance = Math.Abs(frontLength - backLength);

                if (best is null || total > bestTotal || (total == bestTotal && imbalance < bestImbalance))
                {
                    best = (frontCount, backCount);
                    bestTotal = total;
                    bestImbalance = imbalance;
                }
            }
        }

        return best;
    }

    private static int SegmentsLength(IReadOnlyList<string> segments, int start, int count, string separator)
    {
        var sum = 0;
        for (var i = start; i < start + count; i++)
        {
            sum += segments[i].Length;
        }

        return sum + separator.Length * Math.Max(0, count - 1);
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
