namespace EtAlii.Adp.Documents;

/// <summary>
/// What the destination looked like at the instant a publish failed: whether it was there, and
/// whether somebody else's publish was visibly in flight around it.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <see cref="FileHolders"/>, and the half that covers what it cannot.
/// Restart Manager can only name a process still HOLDING the file, and the dominant producer of
/// <c>0x80070497</c> - measured at 185 failures in 400 replaces - is a delete that has already
/// completed and holds nothing. This cannot name anybody, but it separates the cases that matter:
/// a destination that is simply GONE, against one sitting beside another writer's scratch or
/// backup file, which is somebody else's publish caught mid-flight.
/// </para>
/// <para>
/// A <c>File.Replace</c> moves the destination aside to <c>&lt;name&gt;~RF&lt;hex&gt;.TMP</c> for
/// the instant of the swap, and ADP's own scratch files are <c>~adp-*.tmp</c>. Either sitting
/// beside a failed publish says a second publisher was in the middle of one; an empty folder
/// beside a missing destination says it was removed. Architect 1 proposed this instrument, and it
/// is the only one of the two that speaks about an actor that has already let go.
/// </para>
/// <para><b>It may not change what a save does</b>: every failure here answers with words.</para>
/// </remarks>
public static class DestinationState
{
    /// <summary>The Restart Manager backup suffix a File.Replace gives the file it moves aside.</summary>
    private const string ReplaceBackupMarker = "~RF";

    /// <summary>How many neighbouring scratch files are worth naming before the line stops helping.</summary>
    private const int MostSiblingsWorthNaming = 5;

    /// <summary>
    /// One line describing <paramref name="path"/> right now. Never throws: it runs inside a
    /// failure path, and a diagnostic that fails the save it describes would be worse than silence.
    /// </summary>
    public static string Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "no path to look at";
        }

        try
        {
            var present = File.Exists(path);
            var described = present
                ? $"present, {new FileInfo(path).Length} bytes, {File.GetAttributes(path)}"
                : "GONE";

            return $"{described}; {Siblings(path)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"the destination could not be looked at: {exception.GetType().Name} {exception.Message}";
        }
    }

    /// <summary>
    /// The scratch and backup files sitting beside the destination - somebody's publish in flight,
    /// or nobody's.
    /// </summary>
    private static string Siblings(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is not { Length: > 0 } || !Directory.Exists(folder))
        {
            return "its folder is gone too";
        }

        var name = Path.GetFileName(path);
        var siblings = Directory
            .EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .Where(candidate => candidate is { Length: > 0 } &&
                (candidate.StartsWith(AdpFileWriter.TempPrefix, StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(name + ReplaceBackupMarker, StringComparison.OrdinalIgnoreCase)))
            .Take(MostSiblingsWorthNaming + 1)
            .ToArray();

        if (siblings.Length == 0)
        {
            // Said in words rather than left out: no scratch file beside a failed publish is
            // evidence, and an absent phrase reads as an unasked question.
            return "no publish in flight beside it";
        }

        return siblings.Length > MostSiblingsWorthNaming
            ? $"publishes in flight beside it: {string.Join(", ", siblings.Take(MostSiblingsWorthNaming))} and more"
            : $"publishes in flight beside it: {string.Join(", ", siblings)}";
    }
}
