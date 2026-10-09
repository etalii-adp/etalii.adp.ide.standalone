using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Writes the document's <c>thickness</c>: the default scale times <see cref="SankeyGeometry.ThicknessFactor"/>
/// per step, within <see cref="SankeyGeometry.MinimumScale"/> and <see cref="SankeyGeometry.MaximumScale"/>.
/// </summary>
/// <remarks>
/// One number for the whole diagram, because a band's thickness <i>is</i> its value: making one
/// band thicker than its value says would make the diagram lie, so what a reader may change is the
/// scale every band is drawn at - and, per flow, the value itself.
/// </remarks>
public sealed class ScaleSankeyThicknessCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<ScaleSankeyThicknessCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ScaleSankeyThicknessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Steps == 0)
        {
            return Task.FromResult(CommandResult.Failure("A step of nothing changes nothing."));
        }

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            var current = model.Settings.Thickness;
            var scaled = Math.Round(Math.Clamp(current * Math.Pow(SankeyGeometry.ThicknessFactor, command.Steps), SankeyGeometry.MinimumScale, SankeyGeometry.MaximumScale), 2);
            return Math.Abs(scaled - current) < double.Tolerance
                ? SankeyEdit.Refused(command.Steps > 0
                    ? "The bands are already as thick as this diagram draws them."
                    : "The bands are already as thin as this diagram draws them.")
                : SankeyWriter.SetRootKey(document, model.Settings.ThicknessLine, SankeyParser.ThicknessKey, SankeyWriter.Number(scaled));
        });
    }
}
