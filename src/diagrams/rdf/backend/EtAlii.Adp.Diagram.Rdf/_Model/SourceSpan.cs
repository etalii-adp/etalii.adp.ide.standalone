namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Where one triple's own tokens sit in the document: the line range they occupy and, where they
/// share their single line with other content, the character fragment within that line.
/// </summary>
/// <remarks>
/// <para>
/// A triple's <em>own</em> tokens are the ones no other triple states: predicate and object for a
/// statement's first pair and for each <c>;</c> continuation, the object alone for each <c>,</c>
/// continuation. The subject and the terminating <c>.</c> belong to the statement, whose lines
/// <see cref="RdfTriple.Statement"/> records - so a writer removing a statement's last triple
/// knows to take the whole block, and one removing a shared-line triple knows to rewrite exactly
/// one line.
/// </para>
/// <para>
/// The fragment offsets are set when the triple sits on a single line that also carries other
/// substantive content - another triple's tokens, or the statement's subject. They are null when
/// the triple owns its lines: everything else on them is whitespace, a list separator, the
/// statement terminator, or a trailing comment, all of which the splicing writer manages itself.
/// A triple spanning several lines never carries fragment offsets.
/// </para>
/// </remarks>
/// <param name="StartLine">Index of the first line holding the triple's own tokens, zero-based.</param>
/// <param name="EndLine">Index of the last such line, inclusive.</param>
/// <param name="FragmentStart">Column of the first own token within the single shared line, or null where the triple owns its lines.</param>
/// <param name="FragmentEnd">Column just past the last own token within that line, or null where the triple owns its lines.</param>
public readonly record struct SourceSpan(int StartLine, int EndLine, int? FragmentStart, int? FragmentEnd)
{
    /// <summary>Whether the triple owns its lines outright, so a splice may remove or replace them whole.</summary>
    public bool OwnsLines => FragmentStart is null;
}
