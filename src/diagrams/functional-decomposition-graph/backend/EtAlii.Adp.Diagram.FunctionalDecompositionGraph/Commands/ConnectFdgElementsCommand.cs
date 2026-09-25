using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Links <paramref name="FromId"/> to <paramref name="ToId"/> by <paramref name="RelationType"/>, parent to child.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="RelationType">One of the five relation ids in <see cref="FdgRelations"/>.</param>
/// <param name="FromId">The parent, or for Shows the Action.</param>
/// <param name="ToId">The child, or for Shows the page it shows.</param>
/// <param name="ConnectionId">Empty to have one minted, once, as for <see cref="AddFdgElementCommand"/>.</param>
/// <remarks>Refused on the same three checks the canvas makes - see <see cref="FdgConnectVerdict"/>.</remarks>
public sealed record ConnectFdgElementsCommand(string BodyPath, string RelationType, string FromId, string ToId, string ConnectionId = "") : ICommand;
