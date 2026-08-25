namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// What came of applying a property value.
/// </summary>
/// <param name="Error">
/// Empty when the value was applied. Otherwise a sentence for the user, which the grid shows
/// while putting the old value back - a field that silently keeps a rejected value is worse
/// than one that refuses out loud.
/// </param>
public sealed record ContextPropertyResult(string Error = "")
{
    public bool IsSuccess => Error.Length == 0;

    public static ContextPropertyResult Success { get; } = new();

    public static ContextPropertyResult Failure(string error) => new(error);
}
