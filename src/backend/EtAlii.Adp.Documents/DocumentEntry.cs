namespace EtAlii.Adp.Documents;

/// <summary>One loaded document of a diagram type: its body, the model read from it, and why it could not be read, if it could not.</summary>
/// <param name="Document">The document's own body, which every edit changes.</param>
/// <param name="Model">What the module's parser read from it.</param>
/// <param name="Unreadable">Empty for a document that was read; otherwise why its body could not be read.</param>
/// <remarks>
/// <para>
/// <b>One record and one failure field for every module</b> (code-structure FR-029). Thirteen modules
/// each had their own, with the failure called <c>Error</c> in seven, <c>Unreadable</c> in six and a
/// <c>bool</c> in one.
/// </para>
/// <para>
/// <b>An unreadable body is not a missing one.</b> Both open empty, but the emptiness of an
/// unreadable one is not the document, so a save refuses to write it over the only copy on disk.
/// </para>
/// </remarks>
public sealed record DocumentEntry<TBody, TModel>(TBody Document, TModel Model, string Unreadable = "")
{
    /// <summary>Whether this entry holds the document's content, rather than a stand-in for one that could not be read.</summary>
    public bool IsUsable => Unreadable.Length == 0;
}
