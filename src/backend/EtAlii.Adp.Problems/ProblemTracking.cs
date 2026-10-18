using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Problems;

/// <summary>
/// The watching of a project's problems as a part of a connection's project session: the project's
/// problems are kept current from its files once a connection has it open (errors-and-warnings-panel
/// Requirement 5). Releasing it lets nothing go: a project's problems stay tracked for as long as the
/// host runs, as they did before the scope, so the next connection finds them current.
/// </summary>
internal sealed class ProblemTracking(ProblemMaintenance maintenance) : IProjectSessionPart
{
    private static readonly NoProblemTracking Kept = new();

    public IDisposable Acquire(ProjectRoot root)
    {
        maintenance.Track(root.FullPath);
        return Kept;
    }
}
