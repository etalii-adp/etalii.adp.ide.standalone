using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and they are different questions.</b> The parser answers *what could I read*,
/// and records what it could not. The rule set answers *what does the notation forbid*, which
/// needs the whole document - a second parent cannot be seen from one entry. This puts the two
/// together, which is the only thing a caller wants.
/// </para>
/// <para>
/// <b>A connect command asks the rule set rather than this.</b> Validation is about a document
/// that already exists; a refusal is about a link that does not yet. They share the table
/// (<see cref="FdgRelations"/>) and nothing else, so a scripted request cannot write what the
/// canvas would not offer while a document that already contains it still opens and reports it.
/// </para>
/// </remarks>
public static class FdgValidator
{
    /// <summary>Everything wrong with the document, parse problems and rule breaches together.</summary>
    public static IReadOnlyList<FdgBreach> Validate(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return FdgRuleSet.Breaches(FdgParser.Parse(document));
    }

    /// <summary>Everything wrong with a model already parsed.</summary>
    /// <remarks>
    /// Offered beside the text overload because a session holds the model already, and parsing a
    /// second time to validate would read the same document twice per change.
    /// </remarks>
    public static IReadOnlyList<FdgBreach> Validate(FdgModel model) => FdgRuleSet.Breaches(model);
}
