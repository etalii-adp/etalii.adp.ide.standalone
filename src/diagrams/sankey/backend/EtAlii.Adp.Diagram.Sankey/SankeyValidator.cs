using EtAlii.Adp.Diagram.Modules;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Reads a document and reports what is wrong with it, without ever refusing to read it, through
/// the seam every diagram type's rules reach the Errors and Warnings panel by.
/// </summary>
/// <remarks>
/// <para>
/// <b>The findings are the DISL definition's</b> (<see cref="SankeyDefinition.Findings"/>): their codes,
/// severities, sentences and lines, listed in the order of their lines. This class only reads the
/// document and hands each finding to the panel.
/// </para>
/// <para>
/// <b>What is lost is an error, what is merely untidy a warning.</b> A node without an id, a flow to
/// a node that is not there, a duplicate or a flow to itself are not drawn; a flow without a value,
/// an unknown colour or a flow running backwards still draw what the author meant.
/// </para>
/// </remarks>
public sealed class SankeyValidator : IDiagramValidator
{
    /// <inheritdoc />
    public ToolOrigin Origin => Diagram.Sankey.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DiagramProblem> problems =
        [
            .. SankeyDefinition.Findings(SankeyReader.Outcome(request.Document)).Select(finding => new DiagramProblem(
                DislDiagramDefinition.SeverityOf(finding),
                finding.Message,
                finding.Code,
                new DiagramProblemLineLocation((uint)Math.Max(1, finding.Line ?? 1)))),
        ];
        return ValueTask.FromResult(problems);
    }
}
