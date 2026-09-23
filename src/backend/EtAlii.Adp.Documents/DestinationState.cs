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
    /// <param name="ourScratch">
    /// The scratch file THIS publish was using, when there is one. Named so the line can tell a
    /// reader's own publish from somebody else's - see <see cref="Siblings"/>.
    /// </param>
    public static string Describe(string path, string? ourScratch = null)
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

            return $"{described}; {Siblings(path, ourScratch)}";
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
    /// <remarks>
    /// <b>OUR OWN SCRATCH FILE IS ALWAYS ONE OF THESE, AND SAYING SO IS THE POINT.</b> This runs
    /// inside the failing publish, BEFORE that publish deletes its temporary - so exactly one
    /// <c>~adp-*</c> file is the expected state of every failure, and a line that merely listed it
    /// read as "another publisher was here" while describing the reader's own process. Architect 1
    /// read the fourth field occurrence that way and reported a second publisher to itself before
    /// catching it, which is how the phrasing was found to be at fault rather than the query.
    /// </remarks>
    private static string Siblings(string path, string? ourScratch)
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

        var ours = ourScratch is { Length: > 0 } ? Path.GetFileName(ourScratch) : null;
        var theirs = siblings
            .Where(sibling => !string.Equals(sibling, ours, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var mine = ours is not null && siblings.Any(sibling => string.Equals(sibling, ours, StringComparison.OrdinalIgnoreCase))
            ? $"our own {ours}"
            : null;

        if (theirs.Length == 0)
        {
            // Said in words rather than left out: NOBODY ELSE publishing beside a failed publish is
            // evidence, and an absent phrase reads as an unasked question. It only became a real
            // negative once our own scratch file stopped being counted as somebody.
            return mine is null
                ? "nobody else was publishing beside it"
                : $"nobody else was publishing beside it, only {mine}";
        }

        var named = theirs.Length > MostSiblingsWorthNaming
            ? $"{string.Join(", ", theirs.Take(MostSiblingsWorthNaming))} and more"
            : string.Join(", ", theirs);
        return mine is null
            ? $"SOMEBODY ELSE was publishing beside it: {named}"
            : $"SOMEBODY ELSE was publishing beside it: {named}, besides {mine}";
    }
}
