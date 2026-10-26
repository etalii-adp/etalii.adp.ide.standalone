using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// What <see cref="SupplyChainParser"/> could not read, handed to the definition as the reader's findings: each of the
/// model's <see cref="SupplyChainModel.Problems"/> as <see cref="DislReaderFinding.Unreadable"/> with its sentence and its
/// one-based line, which the definition reports as <c>supply-chain.unreadable</c>.
/// </summary>
/// <remarks>
/// <b>The sentence is the parser's, unchanged</b>: an unknown key, a stage this module does not know, a number that will
/// not read, an entry that is not a mapping, a header, a document that is not YAML. The definition gives it a code and a
/// severity; it does not reword it.
/// </remarks>
internal static class SupplyChainReaderFindings
{
    /// <summary>The reader's findings of <paramref name="model"/>, in the order the parser found them.</summary>
    public static IReadOnlyList<DislReaderFinding> Of(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. model.Problems.Select(problem => DislReaderFinding.Unreadable(problem.Message, problem.Line + 1))];
    }
}
