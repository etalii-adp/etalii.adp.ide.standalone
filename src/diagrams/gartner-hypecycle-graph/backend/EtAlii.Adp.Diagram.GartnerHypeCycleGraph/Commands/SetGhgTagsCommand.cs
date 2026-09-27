using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Sets a trend's or a trigger's tags (Requirement 8.1, 5.1).</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="TrendId">The trend or trigger.</param>
/// <param name="Tags">Comma-separated, as the grid's LINE editor sends them. Blank entries and repeats are dropped.</param>
public sealed record SetGhgTagsCommand(string BodyPath, string TrendId, string Tags) : ICommand
{
    /// <summary>The tags the text names, trimmed, in order, each once.</summary>
    public IReadOnlyList<string> Parsed =>
        [.. Tags.Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.Ordinal)];
}
