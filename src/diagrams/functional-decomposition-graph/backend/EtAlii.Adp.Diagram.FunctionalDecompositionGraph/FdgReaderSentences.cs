using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The reader's own sentences, handed to the constraint evaluator as <see cref="DislReaderFinding"/>s on
/// their lines: a key the reader does not read, a <c>name</c> on a Comment, an entry that is not a
/// mapping, a number that does not read, the header's sentences, and a body that is not YAML (findings
/// B6, B7, B2 and B8 of functional-decomposition-graph-disl-fbl).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for gap 6</b> of the companion: the definition's <c>typeMap</c> keeps host attributes out
/// of CEL and the constraints, so neither language can word these. Until the body is read through the
/// binding (task 9) they are read from <see cref="FdgParser"/>'s problems, in the order the parser gives
/// them: the elements, then the connections, then the header.
/// </para>
/// <para>
/// <b>An entry of a type the notation does not have is the definition's to report</b>, by its rules
/// <c>unknownElementType</c> and <c>unknownRelationType</c>, so the parser's sentence for it is not handed on;
/// <see cref="FdgFindingOrder"/> puts the definition's where the parser's stood.
/// </para>
/// </remarks>
internal static class FdgReaderSentences
{
    /// <summary>How the parser begins the sentence of a body that is not YAML; what follows is the reason.</summary>
    private const string NotYaml = "The document could not be read as YAML: ";

    /// <summary>The reader's findings of <paramref name="model"/>, in reading order.</summary>
    public static IReadOnlyList<DislReaderFinding> Of(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. Handed(model).Select(Finding)];
    }

    /// <summary>The parser's problems this hands on, in the parser's order: every one but an unknown type's.</summary>
    private static IEnumerable<ParseProblem> Handed(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Problems.Where(problem => !IsTypeSentence(model, problem));
    }

    /// <summary>Whether <paramref name="problem"/> is the parser's sentence for an entry of a type the notation does not have.</summary>
    public static bool IsTypeSentence(FdgModel model, ParseProblem problem)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(problem);
        return model.Elements.Any(element => element.Range.Start == problem.Line && !FdgElementTypes.IsKnown(element.Type)
                                             && problem.Message == $"`{element.Type}` is not an element type this module knows.")
               || model.Connections.Any(connection => connection.Range.Start == problem.Line && !FdgConnectionTypes.IsKnown(connection.Type)
                                                      && problem.Message == $"`{connection.Type}` is not a relation this module knows.");
    }

    private static DislReaderFinding Finding(ParseProblem problem) =>
        problem.Message.StartsWith(NotYaml, StringComparison.Ordinal)
            ? DislReaderFinding.NotParsed(problem.Message[NotYaml.Length..], problem.Line + 1)
            : DislReaderFinding.Unreadable(problem.Message, problem.Line + 1);
}
