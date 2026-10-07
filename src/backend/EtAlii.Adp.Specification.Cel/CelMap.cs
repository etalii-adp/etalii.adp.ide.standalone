namespace EtAlii.Adp.Specification.Cel;

/// <summary>The map type CEL values use: insertion-ordered, string keys.</summary>
public sealed class CelMap() : Dictionary<string, object?>(StringComparer.Ordinal);
