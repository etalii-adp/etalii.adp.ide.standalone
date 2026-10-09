using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>What a new activity file holds, for the Add dialog (Requirement 9.7).</summary>
public sealed class AadDocumentFactory : IDiagramDocumentFactory
{
    /// <summary>The version this module writes in a new file's header.</summary>
    public const int Version = 1;

    public DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return EmptyDocument("\r\n");
    }

    /// <summary>
    /// The header line alone. No list is written empty: the binding creates each with its first
    /// entry and removes it with its last, because an empty YAML list is a flow collection that
    /// nothing can be added to by a splice (the definition's research note R5). CRLF, the house
    /// style, since a new file has no style of its own to keep.
    /// </summary>
    public static string EmptyDocument(string lineEnding)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);
        return $"agent-activity-diagram: {Version}{lineEnding}";
    }
}
