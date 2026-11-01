namespace EtAlii.Adp.Specification.Fbl;

/// <summary>What an undo or a redo did: the splices it applied, or why it was refused.</summary>
internal abstract record UndoResult
{
    public sealed record Done(IReadOnlyList<Splice> Splices) : UndoResult;

    public sealed record Refused(string Reason) : UndoResult;
}
