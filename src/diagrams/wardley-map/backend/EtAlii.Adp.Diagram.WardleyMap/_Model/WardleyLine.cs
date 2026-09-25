
namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One line of an `.owm` document with its 1-based number - what the parser reads and what a
/// problem points at.
/// </summary>
/// <remarks>
/// 1-based deliberately, to match <see cref="DiagramProblemLineLocation"/>. The reference
/// OnlineWardleyMaps parser numbers its own errors from 0, so anything comparing the two has
/// to convert; keeping this side 1-based means the conversion happens once, where that
/// parser's output is read, rather than everywhere a problem is raised.
/// </remarks>
public sealed record WardleyLine(string Text, uint Number);
