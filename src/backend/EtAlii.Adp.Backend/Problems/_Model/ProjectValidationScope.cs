namespace EtAlii.Adp.Backend.Problems;

/// <summary>The whole project - Validate all.</summary>
public sealed record ProjectValidationScope(string RootPath) : ValidationScope(RootPath);
