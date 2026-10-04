using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Adds <paramref name="Steps"/> steps - negative to take them away - to a node's quantity or a flow's
/// volume. One step is the entry's own <c>step</c>, or <see cref="SupplyChainGeometry.DefaultStep"/>.
/// </summary>
public sealed record StepSupplyChainValueCommand(string BodyPath, string EntryId, int Steps) : ICommand;
