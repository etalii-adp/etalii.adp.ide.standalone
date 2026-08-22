namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// What came of trying to create a file. A taken name is its own outcome rather than a
/// failure message, because the caller answers it differently: the user picks another name,
/// and nothing is ever renamed on their behalf.
/// </summary>
public abstract record AdpFileWriteResult
{
    public sealed record Created(string FullPath) : AdpFileWriteResult;

    public sealed record NameTaken : AdpFileWriteResult;

    public sealed record Failed(string Message) : AdpFileWriteResult;
}
