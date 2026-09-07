namespace EtAlii.Adp.Problems;

/// <summary>The whole project - Validate all.</summary>
public sealed record ProjectValidationScope(string RootPath) : ValidationScope(RootPath);
