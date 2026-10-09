namespace EtAlii.Adp.Documents;

/// <summary>
/// What came of applying one edit to a document: the edited document, or the reason the edit no
/// longer applies to the document it was handed.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because an edit may be applied twice, to two different documents.</b>
/// <see cref="WritableDocumentLifecycle{TDocument}.Save(string, TDocument, Func{TDocument, DocumentEdit{TDocument}})"/>
/// applies the edit to the document the caller read, unless another program changed the file since -
/// then it applies the same edit to what the file holds now. The second document may no longer have
/// the entry the edit was for, and that is an answer rather than an exception: the user is told, and
/// nothing is written.
/// </para>
/// <para>
/// A sealed class with two named factories, after <see cref="DocumentSaveResult"/> beside it and for
/// its reason: a refusal that could be mistaken for an edited document is the wrong default.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">What the store caches.</typeparam>
public sealed class DocumentEdit<TDocument>
    where TDocument : class
{
    private DocumentEdit(TDocument? document, string refusal)
    {
        Document = document;
        Refusal = refusal;
    }

    /// <summary>The edit applied, giving <paramref name="document"/>.</summary>
    public static DocumentEdit<TDocument> Applied(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentEdit<TDocument>(document, "");
    }

    /// <summary>The edit does not apply, with a reason meant for the user.</summary>
    public static DocumentEdit<TDocument> Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DocumentEdit<TDocument>(null, reason);
    }

    /// <summary>The edited document, or null when the edit was refused.</summary>
    public TDocument? Document { get; }

    /// <summary>Empty when the edit applied; a sentence for the user when it did not.</summary>
    public string Refusal { get; }
}
