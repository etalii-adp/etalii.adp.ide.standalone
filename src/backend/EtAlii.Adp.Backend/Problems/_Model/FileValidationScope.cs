namespace EtAlii.Adp.Backend.Problems;

/// <summary>One diagram file, named relative to the root.</summary>
public sealed record FileValidationScope(string RootPath, string RelativePath) : ValidationScope(RootPath);
