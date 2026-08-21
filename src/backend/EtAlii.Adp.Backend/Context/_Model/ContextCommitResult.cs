namespace EtAlii.Adp.Backend.Context;

/// <summary>The outcome of actually performing an action, once the user confirmed it.</summary>
public sealed record ContextCommitResult(bool Completed, string Error = "")
{
    public static ContextCommitResult Succeeded { get; } = new(true);

    public static ContextCommitResult Failed(string error) => new(false, error);
}
