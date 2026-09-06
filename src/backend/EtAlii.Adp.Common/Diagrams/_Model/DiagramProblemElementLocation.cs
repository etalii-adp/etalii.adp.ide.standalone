namespace EtAlii.Adp.Common;

/// <summary>The problem sits on one diagram element, named by the module's own stable element id.</summary>
public sealed record DiagramProblemElementLocation(string Id) : DiagramProblemLocation;
