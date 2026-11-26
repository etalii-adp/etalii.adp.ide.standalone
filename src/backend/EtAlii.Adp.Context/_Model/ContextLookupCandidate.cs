namespace EtAlii.Adp.Context;

/// <summary>One element a lookup offers: the value its commit receives, its name, and its document's name and folder.</summary>
public sealed record ContextLookupCandidate(string Value, string Name, string DocumentName, string Folder);
