using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// What <see cref="SupplyChainReading"/> could not read, handed to the definition as the reader's findings: each of the
/// model's <see cref="SupplyChainModel.Problems"/> as <see cref="DislReaderFinding.Unreadable"/> with its sentence and its
/// one-based line, which the definition reports as <c>supply-chain.unreadable</c>.
/// </summary>
/// <remarks>
/// <b>The sentence is the reading's, composed from what the binding reads in the words the module always used</b>: an
/// unknown key from <c>unknownKeys</c>, an entry that is not a mapping from the <c>Unreadable</c> rule that took it, a
/// stage and a number that will not read from the slot's text, the three header cases from the document's
/// <c>version</c>, and a document that is not YAML. The definition gives it a code and a severity; it does not reword it.
/// </remarks>
internal static class SupplyChainReaderFindings
{
    /// <summary>The reader's findings of <paramref name="model"/>, in the order the reading lists them.</summary>
    public static IReadOnlyList<DislReaderFinding> Of(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. model.Problems.Select(problem => DislReaderFinding.Unreadable(problem.Message, problem.Line + 1))];
    }
}
