namespace EtAlii.Adp.Problems;

/// <summary>One folder and everything beneath it, named relative to the root.</summary>
public sealed record FolderValidationScope(string RootPath, string RelativePath) : ValidationScope(RootPath);
