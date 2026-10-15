namespace EtAlii.Adp.Documents;

/// <summary>What a parser read from a body: the model, what it passed over, and whether the model holds the document.</summary>
/// <param name="Model">What was read; for a body that could not be read at all, the model of an empty document.</param>
/// <param name="Problems">What the parser passed over or could not read, in the order it found them.</param>
/// <param name="Unreadable">Empty for a body that was read; otherwise why it could not be, in one sentence.</param>
/// <typeparam name="TModel">The module's model.</typeparam>
/// <remarks>
/// <para>
/// <b>One parse outcome for every module</b> (code-structure FR-049). Parsers report in four styles
/// today: an exception thrown to the store, an entry passed over in silence, a string or a <c>bool</c>
/// on the entry, and a list of findings on the model. This record is the one they move to.
/// </para>
/// <para>
/// <b>A model with problems is still usable.</b> A document with a bad entry draws every entry that
/// is good, so problems alone never make it unusable; only a body that could not be read at all is,
/// and nothing is written to it until it can be.
/// </para>
/// </remarks>
public sealed record ParseOutcome<TModel>(TModel Model, IReadOnlyList<ParseProblem> Problems, string Unreadable = "")
{
    /// <summary>Whether <see cref="Model"/> holds the document, rather than a stand-in for one that could not be read.</summary>
    public bool IsUsable => Unreadable.Length == 0;
}
