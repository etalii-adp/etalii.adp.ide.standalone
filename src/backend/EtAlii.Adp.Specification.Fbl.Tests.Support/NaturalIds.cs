
namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The DISL id derivations the vendored fixtures name their elements by, standing in for DISL,
/// which this library does not implement (Requirement 4.4). Each mirrors the <c>persistence.ids</c>
/// of the specification in etalii-adp/etalii.adp <c>definitions/diagrams/</c>: natural ids with the
/// type's prefix (databricks-job.dis, databricks-pipeline.dis, causal-loop-diagram.dis, whose
/// keys are a variable's name, a link's cause and effect names and a loop's identifier).
/// </summary>
public static class NaturalIds
{
    public static Func<IdRequest, string?> For(string binding) => request => (binding, request.Rule) switch
    {
        ("cld", "variable") => $"variable:{Attribute(request, "name")}",
        ("cld", "link") => $"link:{Attribute(request, "causeName")}|{Attribute(request, "effectName")}",
        ("cld", "loop") => $"loop:{Attribute(request, "identifier")}",
        ("job", "task") => $"task:{Attribute(request, "key")}",
        ("job", "cluster") => $"cluster:{Attribute(request, "key")}",
        ("job", "dependency") => $"edge:{Unprefixed(request.Source)}->{Unprefixed(request.Target)}",
        ("settings", "pipeline") => "pipeline",
        ("settings", "library") => $"library:{Attribute(request, "path")}",
        // The one binding the bundle, job and pipeline readings of a Databricks file share (FBL 0.10), named as the
        // fixtures name them, which are the standalone module's ids: the job reading's diagram is the job itself.
        ("configuration", "bundle") => "bundle",
        ("configuration", "jobResource" or "pipelineResource") => $"resource:{Attribute(request, "resourceKind")}/{Attribute(request, "key")}",
        ("configuration", "target") => $"target:{Attribute(request, "name")}",
        ("configuration", "job") => "job",
        ("configuration", "task") => $"task:{Attribute(request, "key")}",
        ("configuration", "cluster") => $"cluster:{Attribute(request, "key")}",
        ("configuration", "dependency") => $"edge:{Unprefixed(request.Source)}->{Unprefixed(request.Target)}",
        ("configuration", "pipeline" or "settings") => "pipeline",
        ("configuration", "notebookLibrary" or "fileLibrary" or "globLibrary" or "settingsLibrary") => $"library:{Attribute(request, "path")}",
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

    /// <summary>
    /// The binding type a binding's tool type maps <c>as: "diagram"</c> (DISL 11.2), whose element's attributes are a
    /// fixture's <c>read.document</c> (FBL 0.7 §15.3): read here, as the ids are, from the specifications in etalii.adp.
    /// Null for a binding whose tool type maps none.
    /// </summary>
    public static string? DocumentType(string binding) => binding switch
    {
        "configuration" => "Job",
        _ => null,
    };

    /// <summary>Whether a binding's tool type keeps DISL's default deletion policy, which clears the references to a removed element.</summary>
    public static bool UnsetsReferences(string binding) => binding == "aad";

    private static string Attribute(IdRequest request, string name) =>
        request.Attributes.TryGetValue(name, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";

    private static string? Unprefixed(string? id) => id?[(id.IndexOf(':') + 1)..];
}
