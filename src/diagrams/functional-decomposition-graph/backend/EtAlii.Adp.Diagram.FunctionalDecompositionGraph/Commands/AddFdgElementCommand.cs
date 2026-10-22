using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Adds one element of <paramref name="ElementType"/> centred on (<paramref name="X"/>, <paramref name="Y"/>).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementType">One of the document's spellings of the element types (<c>ui-element</c> and the rest).</param>
/// <param name="X">The centre where it was dropped - the library draws from the centre.</param>
/// <param name="Y">The centre where it was dropped.</param>
/// <param name="ElementId">
/// Empty to have one minted. The handler mints it ONCE and returns the minted command as the redo, so
/// a redone element keeps the id later commands refer to.
/// </param>
public sealed record AddFdgElementCommand(string BodyPath, string ElementType, double X, double Y, string ElementId = "") : IDocumentEditCommand;
