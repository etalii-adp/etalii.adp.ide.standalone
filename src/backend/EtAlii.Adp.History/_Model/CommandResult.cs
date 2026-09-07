namespace EtAlii.Adp.History;

/// <summary>
/// What came of executing an <see cref="ICommand"/>: success (optionally with the command
/// that reverses it) or a failure carrying a message meant for the user.
/// </summary>
/// <remarks>
/// Failure is modelled as a value rather than an exception because a rejected command -
/// a name that is already taken, a file that has since vanished - is an expected outcome
/// on this path, not an exceptional one. Exceptions stay reserved for programming errors,
/// such as dispatching a command that has no registered handler.
/// </remarks>
public sealed class CommandResult
{
    private CommandResult(bool isSuccess, string error, ICommand? inverse, string warning = "")
    {
        IsSuccess = isSuccess;
        Error = error;
        Inverse = inverse;
        Warning = warning;
    }

    public bool IsSuccess { get; }

    /// <summary>The reason the command was rejected; empty when <see cref="IsSuccess"/>.</summary>
    public string Error { get; }

    /// <summary>
    /// The command that undoes this one, or <c>null</c> when there is nothing to undo.
    /// Always <c>null</c> on a failure: a command that did not run has nothing to reverse.
    /// </summary>
    public ICommand? Inverse { get; }

    /// <summary>
    /// Something the user should know although the command SUCCEEDED; empty when there is
    /// nothing to say.
    /// </summary>
    /// <remarks>
    /// The case this exists for: the edit reached the document, but something beside it did
    /// not - a layout sidecar that could not be written, so the element moved and its new
    /// position was not kept. Failing the command would be wrong, because the edit landed and
    /// refusing it would lose work the user can see. Saying nothing was what happened before,
    /// and it meant a drag silently did not persist.
    /// </remarks>
    public string Warning { get; }

    /// <summary>Succeeded, with nothing for the history to record.</summary>
    public static CommandResult Success() => new(true, "", null);

    /// <summary>Succeeded, and <paramref name="inverse"/> is what puts it back.</summary>
    public static CommandResult Success(ICommand inverse)
    {
        ArgumentNullException.ThrowIfNull(inverse);
        return new CommandResult(true, "", inverse);
    }

    /// <summary>
    /// Succeeded, with something the user should know anyway. An empty
    /// <paramref name="warning"/> is the same as the plain success.
    /// </summary>
    public static CommandResult Success(ICommand inverse, string warning)
    {
        ArgumentNullException.ThrowIfNull(inverse);
        ArgumentNullException.ThrowIfNull(warning);
        return new CommandResult(true, "", inverse, warning);
    }

    /// <summary>Rejected, for a reason the caller can show to the user.</summary>
    public static CommandResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CommandResult(false, error, null);
    }
}
