namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>What <c>read</c> delivers: the elements and relations with their source spans, the findings, and whether the body is unreadable.</summary>
public sealed record PluginReadResult(IReadOnlyList<FblElement> Elements, IReadOnlyList<Finding> Findings, bool Unreadable);
