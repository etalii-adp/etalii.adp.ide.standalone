namespace EtAlii.Adp.Context;

/// <summary>The outcome of actually performing an action, once the user confirmed it.</summary>
/// <param name="CreatedFullPath">
/// Absolute location of what the action created, for an action that creates something.
/// It stays inside the backend: the service turns it into a project-relative path before
/// it reaches a client, which is the one place that rule is applied.
/// </param>
public sealed record ContextCommitResult(
    bool Completed,
    string Error = "",
    string CreatedFullPath = "")
{
    public static ContextCommitResult Succeeded { get; } = new(true);

    public static ContextCommitResult Failed(string error) => new(false, error);

    /// <summary>Succeeded, having created something the client may want to reveal.</summary>
    public static ContextCommitResult Created(string fullPath) => new(true, CreatedFullPath: fullPath);
}
