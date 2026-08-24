namespace EtAlii.Adp.Backend.Diagrams;

public sealed class DiagramDeltasEventArgs(IReadOnlyList<DiagramDelta> deltas) : EventArgs
{
    public IReadOnlyList<DiagramDelta> Deltas { get; } = deltas;
}
