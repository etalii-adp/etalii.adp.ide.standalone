namespace EtAlii.Adp.Documents;

/// <summary>
/// What came of saving a document: whether it landed, and anything the user should know even when
/// it did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two strings rather than one, because the two outcomes are not the same and were being
/// conflated.</b> <see cref="Error"/> means the document is not on disk and the command failed.
/// <see cref="Warning"/> means the document IS on disk and something beside it is not - wardley's
/// identity sidecar is the case that named this - which must not fail the edit and must not pass
/// unmentioned either. This is wardley-map's `WardleyPublishResult` shape, moved here and shared by
/// every store (backend-centralization S3, R3.1).
/// </para>
/// <para>
/// <b>A SEALED RECORD RATHER THAN THE `readonly record struct` WARDLEY HAD, and the difference is
/// the point rather than a detail.</b> The approved design specified the SHAPE - an error and a
/// warning, both empty on success - and the kind was wardley's local choice. A struct has a
/// `default` whose strings are null, and such a value is a usable object that flows through code:
/// `Error != ""` reads as *failed with no message*, an interpolated `Error` prints as empty, and
/// only `Failed` or `Error.Length` throws. A null reference throws at the first access instead,
/// at the point where the mistake was made. <b>For a type whose entire purpose is that failures
/// cannot be ignored, a value that can be mistaken for an answer is the wrong default</b>, and the
/// alternative considered - defaulting the strings to empty - would have converted a crash into a
/// silent success, which is worse.
/// </para>
/// <para>
/// <b>It does not adopt the <see cref="AdpFileWriteResult"/> idiom beside it, deliberately.</b>
/// That one is a closed set of cases because its callers answer them DIFFERENTLY: a taken name
/// means "ask the user for another name" rather than "report a failure". A save has two outcomes
/// and an orthogonal warning, so a case hierarchy would add a type per outcome without adding a
/// decision. Two idioms in one namespace is worth a sentence rather than a unification.
/// </para>
/// <para>
/// <b>There is no `IsSuccess`.</b> Two spellings of one question is how thirteen call sites drift
/// apart, and the ignored-result guard needs one obvious thing to look for.
/// </para>
/// </remarks>
public sealed record DocumentSaveResult
{
    /// <summary>The constructor is private so that every instance comes from a named factory, and
    /// so that neither string can be null in a constructed value.</summary>
    private DocumentSaveResult(string error, string warning)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(warning);

        Error = error;
        Warning = warning;
    }

    /// <summary>Everything landed.</summary>
    public static DocumentSaveResult Ok { get; } = new("", "");

    /// <summary>The document did not land, with a reason meant for the user.</summary>
    /// <remarks>
    /// Named <c>Failure</c> rather than <c>Failed</c> because <see cref="Failed"/> is the question a
    /// caller asks, and one name cannot be both a property and a method on the same type.
    /// </remarks>
    public static DocumentSaveResult Failure(string error) => new(error, "");

    /// <summary>The document landed; something beside it did not.</summary>
    public static DocumentSaveResult WithWarning(string warning) => new("", warning);

    /// <summary>Empty when the document was written; a sentence for the user when it was not.</summary>
    public string Error { get; }

    /// <summary>Empty when there is nothing to say; a sentence for the user alongside a success.</summary>
    public string Warning { get; }

    /// <summary>Whether the document failed to land. The one question a caller asks.</summary>
    public bool Failed => Error.Length > 0;
}
