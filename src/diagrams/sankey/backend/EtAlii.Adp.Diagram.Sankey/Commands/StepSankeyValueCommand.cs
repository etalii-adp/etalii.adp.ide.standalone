using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Adds <paramref name="Steps"/> steps - negative to take them away - to a flow's value, which is
/// its thickness. One step is the flow's own <c>step</c>, or <see cref="StepSankeyValueCommandHandler.DefaultStep"/>.
/// </summary>
public sealed record StepSankeyValueCommand(string BodyPath, string FlowId, int Steps) : ICommand;
