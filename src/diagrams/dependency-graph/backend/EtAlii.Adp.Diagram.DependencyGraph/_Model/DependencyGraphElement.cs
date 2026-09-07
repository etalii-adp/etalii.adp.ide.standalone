using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// One node of the graph: a labelled box at an authored position.
/// </summary>
/// <remarks>
/// No date, no duration, no instant. This type is the timeline with time removed, and the
/// horizontal coordinate is the whole of what the timeline expressed through begin and end: a
/// number the author owns, meaning nothing but where the box sits.
/// </remarks>
/// <param name="Id">The stable identifier, from the document. Identity lives in the file because the schema is ADP's own.</param>
/// <param name="Label">What it is called.</param>
/// <param name="X">Its horizontal coordinate, in canvas units - authored, never derived.</param>
/// <param name="Row">Which row it sits on - a placement grid, never a lane.</param>
/// <param name="Range">The lines that declare it, for the writer and for diagnostics.</param>
public sealed record DependencyGraphElement(
    string Id,
    string Label,
    double X,
    int Row,
    LineRange Range);
