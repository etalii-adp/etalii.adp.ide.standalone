using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>One thing wrong with a document, with the zero-based line it was found on.</summary>
public sealed record SupplyChainBreach(string RuleId, string Message, int Line, bool IsWarning);

/// <summary>The rules a <c>.supply</c> document is checked against, each with its id.</summary>
public static class SupplyChainRuleIds
{
    public const string UnreadableEntry = "supply-chain.unreadable";
    public const string MissingId = "supply-chain.missing-id";
    public const string DuplicateId = "supply-chain.duplicate-id";
    public const string DanglingFlow = "supply-chain.dangling-flow";
    public const string SelfFlow = "supply-chain.self-flow";
    public const string UnknownGroup = "supply-chain.unknown-group";
    public const string EmptyGroup = "supply-chain.empty-group";
}

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="ConstraintEvaluator"/>): its constraints over the
/// document's DISL model, with what the reading could not read handed in as the reader's findings
/// (<see cref="SupplyChainReaderFindings"/>), in one list by line (<see cref="SupplyChainFindings"/>).
/// </para>
/// <para>
/// <b>What is lost is an error, what is merely untidy a warning</b>, as the definition states each code's severity. A
/// flow to a node that is not there, a duplicate id or a self-flow are not drawn; an unreadable line, an unknown group
/// or an empty one still draw everything the author meant.
/// </para>
/// </remarks>
public sealed class SupplyChainValidator : IDiagramValidator
{
    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems =
            [.. SupplyChainDefinition.Findings(SupplyChainReading.Parse(request.Document)).Select(SupplyChainFindings.ToProblem)];
        return ValueTask.FromResult(problems);
    }

    /// <summary>Everything wrong with a model already parsed.</summary>
    public static IReadOnlyList<SupplyChainBreach> Validate(SupplyChainModel model) =>
        [.. SupplyChainDefinition.Findings(model).Select(SupplyChainFindings.ToBreach)];
}
