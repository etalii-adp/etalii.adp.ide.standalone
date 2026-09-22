// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

internal sealed class CachedProjectProblems(string rootPath)
{
    public object Gate { get; } = new();
    public string RootPath { get; } = rootPath;
    public List<StoredProblem> Problems { get; } = [];
    public ProjectProblemSetState State { get; set; } = ProjectProblemSetState.NeverValidated;
    public Timer? WriteTimer { get; set; }

    /// <summary>
    /// Whether this entry holds a change nobody has written yet. <b>The timer field cannot answer
    /// that</b>: the debounce callback clears it on its way to deciding not to write, after which
    /// the flush on Dispose reads "no timer" as "nothing owed" and skips the entry - so the last
    /// change is lost by both. Set when a write is scheduled, cleared when one happens, and read
    /// under <see cref="Gate"/> like everything else here.
    /// </summary>
    public bool WritePending { get; set; }
}
