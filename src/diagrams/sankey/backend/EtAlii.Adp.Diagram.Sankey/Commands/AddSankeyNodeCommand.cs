using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Adds one node where it was dropped: in the column nearest (<paramref name="X"/>), at its place by (<paramref name="Y"/>).</summary>
/// <param name="NodeId">Empty to have one minted; the handler mints it once and returns the minted command as the redo.</param>
public sealed record AddSankeyNodeCommand(string BodyPath, double X, double Y, string NodeId = "") : ICommand;
