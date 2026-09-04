using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// A construct the parser recognised as present but does not model - an unmodelled root key, an
/// unknown resource kind. It renders as a generic box, no writer ever touches its lines, and so
/// it survives every edit byte for byte (Requirement 2.4).
/// </summary>
/// <param name="Path">Where it sits, as a dotted breadcrumb - e.g. <c>sync</c> or <c>resources.experiments</c>.</param>
/// <param name="Key">The key itself, the last segment of <paramref name="Path"/>.</param>
/// <param name="Lines">The lines that declare it.</param>
public sealed record UnknownNode(string Path, string Key, LineRange Lines);
