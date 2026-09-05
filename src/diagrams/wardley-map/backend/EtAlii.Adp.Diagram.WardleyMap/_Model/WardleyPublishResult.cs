namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What came of publishing a map: whether the document itself landed, and anything the user
/// should know even when it did.
/// </summary>
/// <remarks>
/// Two strings rather than one because the two outcomes are not the same and were being
/// conflated. <see cref="Error"/> means the document is not on disk and the command failed.
/// <see cref="Warning"/> means the document IS on disk and something beside it is not - the
/// identity sidecar - which must not fail the edit and must not pass unmentioned either.
/// </remarks>
/// <param name="Error">Empty when the document was written; a sentence for the user when it was not.</param>
/// <param name="Warning">Empty when there is nothing to say; a sentence for the user alongside a success.</param>
public readonly record struct WardleyPublishResult(string Error, string Warning)
{
    /// <summary>Everything landed.</summary>
    public static WardleyPublishResult Ok { get; } = new("", "");

    /// <summary>The document landed; something beside it did not.</summary>
    public static WardleyPublishResult WithWarning(string warning) => new("", warning);

    /// <summary>The document did not land.</summary>
    public static WardleyPublishResult Failed(string error) => new(error, "");
}
