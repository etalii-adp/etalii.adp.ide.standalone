using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The findings of a diagram's links (FBL 0.23 §17.6, etalii.adp spec 092, FR-009): <c>link.malformed</c>,
/// <c>link.broken-document</c>, <c>link.broken-element</c> and <c>link.ambiguous-document</c>, each at the element that holds
/// the link, for the problems list.
/// </summary>
public static class DislLinkFindings
{
    /// <summary>The host attribute an FBL reading keeps an element's written id in.</summary>
    private const string StoredId = "storedId";

    /// <summary>
    /// The findings of every link <paramref name="diagram"/> holds, resolved in <paramref name="workspace"/>. Without a workspace,
    /// as when no connection has the project open, only a link that does not parse is reported: whether the others resolve is
    /// not known.
    /// </summary>
    public static IReadOnlyList<DiagramProblem> Of(DislDiagram diagram, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var problems = new List<DiagramProblem>();
        foreach (var element in diagram.Elements.Where(element => !element.IsDerived))
        {
            foreach (var attribute in element.Type.Attributes.Values.Where(attribute => attribute.Type == "link"))
            {
                foreach (var text in Texts(element.ValueOf(attribute.Name)))
                {
                    if (FindingOf(text, workspace) is { } finding)
                    {
                        problems.Add(new DiagramProblem(SeverityOf(finding.Code), finding.Message, finding.Code, new DiagramProblemElementLocation(WrittenIdOf(element))));
                    }
                }
            }
        }

        return problems;
    }

    private static LinkFinding? FindingOf(string text, LinkWorkspace? workspace) =>
        workspace is not null ? workspace.Resolve(text).Finding
        : FblLink.Parse(text) is null ? new LinkWorkspace("", []).Resolve(text).Finding
        : null;

    private static IEnumerable<string> Texts(object? value) => value switch
    {
        string { Length: > 0 } text => [text],
        IEnumerable<object?> items => items.OfType<string>().Where(text => text.Length > 0),
        _ => [],
    };

    private static DiagramProblemSeverity SeverityOf(string code) =>
        code == LinkCodes.Malformed ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning;

    private static string WrittenIdOf(DislElement element) =>
        element.HostAttributes.TryGetValue(StoredId, out var id) && id is string { Length: > 0 } written ? written : element.Id;
}
