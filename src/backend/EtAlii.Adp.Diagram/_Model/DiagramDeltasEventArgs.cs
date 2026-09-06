namespace EtAlii.Adp.Diagram;

public sealed class DiagramDeltasEventArgs(IReadOnlyList<DiagramDelta> deltas) : EventArgs
{
    public IReadOnlyList<DiagramDelta> Deltas { get; } = deltas;
}
