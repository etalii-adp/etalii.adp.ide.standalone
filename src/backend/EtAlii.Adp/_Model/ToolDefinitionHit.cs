namespace EtAlii.Adp;

/// <summary>
/// One definition a <see cref="ToolDefinitionScan"/> found: the definition itself, the class
/// that declared it - kept so a family-specific rule rejecting the definition later can still
/// name the class in its warning - and the assembly it came from, which duplicate detection
/// uses for its ordinal tie-break.
/// </summary>
internal sealed record ToolDefinitionHit<T>(T Definition, Type DeclaringType, string AssemblyName);
