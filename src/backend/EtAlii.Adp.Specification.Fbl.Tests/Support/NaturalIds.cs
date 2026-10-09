namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The DISL id derivations the vendored fixtures name their elements by, standing in for DISL,
/// which this library does not implement (Requirement 4.4). Each mirrors the <c>persistence.ids</c>
/// of the specification in etalii-adp/etalii.adp <c>definitions/diagrams/</c>: natural ids with the
/// type's prefix (databricks-job.dis, databricks-pipeline.dis), and the causal loop's links and
/// loops by their ends and identifier as its fixtures address them.
/// </summary>
internal static class NaturalIds
{
    public static Func<IdRequest, string?> For(string binding) => request => (binding, request.Rule) switch
    {
        ("cld", "link") => $"link:{request.Source}|{request.Target}",
        ("cld", "loop") => $"loop:{Attribute(request, "identifier")}",
        ("job", "task") => $"task:{Attribute(request, "key")}",
        ("job", "cluster") => $"cluster:{Attribute(request, "key")}",
        ("job", "dependency") => $"edge:{Unprefixed(request.Source)}->{Unprefixed(request.Target)}",
        ("settings", "pipeline") => "pipeline",
        ("settings", "library") => $"library:{Attribute(request, "path")}",
        ("freeplane", "branch") => $"branch:{request.Source}->{request.Target}",
        ("workspace", "relationship") => $"{request.Source}->{request.Target}",
        // The agent activity diagram (DISL 0.4, persistence.view.bind): the root is the diagram, and a
        // locked position and a group state are view data, keyed by the element they name.
        ("aad", "diagram") => "diagram",
        ("aad", "placement") => $"pinned:{Attribute(request, "element")}",
        ("aad", "group") => $"groups:{Attribute(request, "element")}#{Attribute(request, "group")}",
        _ => null,
    };

    /// <summary>
    /// The attributes a binding's tool type gives a date or date-time type, which FBL section 6.3
    /// asks of the host: read here, as the ids are, from the specifications in etalii.adp.
    /// </summary>
    public static IReadOnlySet<string> TimeAttributes(string binding) => binding switch
    {
        "aad" => new HashSet<string>(StringComparer.Ordinal) { "task.updated", "pullRequest.updated" },
        _ => new HashSet<string>(StringComparer.Ordinal),
    };

    /// <summary>Whether a binding's tool type keeps DISL's default deletion policy, which clears the references to a removed element.</summary>
    public static bool UnsetsReferences(string binding) => binding == "aad";

    private static string Attribute(IdRequest request, string name) =>
        request.Attributes.TryGetValue(name, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";

    private static string? Unprefixed(string? id) => id?[(id.IndexOf(':') + 1)..];
}
