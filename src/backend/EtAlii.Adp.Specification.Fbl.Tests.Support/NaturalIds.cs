
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
        // The C4 types (c4-*.dis persistence.ids): a relationship and an interaction have no identifier in the DSL, so their
        // ids are their ends as written and their line; every other element is named by its identifier, or ephemeral without one.
        ("workspace", "relationship") => $"{Attribute(request, "from")}->{Attribute(request, "to")}@{request.Line}",
        ("workspace", "interaction") => $"interaction:{Attribute(request, "from")}->{Attribute(request, "to")}@{request.Line}",
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
        "workspace" => "Workspace",
        _ => null,
    };

    /// <summary>
    /// The further binding types a binding's tool type maps <c>as: "diagram"</c> beside <see cref="DocumentType"/>, and the
    /// name each of their attributes takes on the diagram (DISL 11.2, <c>typeMap</c>), read here from the specifications
    /// in etalii.adp: the C4 types map the selected view's statements (<c>ViewInclude</c>) onto the diagram too, and name the
    /// workspace's name <c>workspaceName</c> and a view's include members <c>includes</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DocumentParts(string binding) => binding switch
    {
        "workspace" => new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Workspace"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "workspaceName" },
            ["ViewInclude"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["members"] = "includes" },
        },
        _ => new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal),
    };

    /// <summary>
    /// Whether a binding's tool type gives an attribute of a binding type <c>many</c> (DISL), which a binding does not
    /// carry: the C4 types' tags, and a view's include and exclude members.
    /// </summary>
    public static Func<string, string, bool>? Many(string binding) => binding switch
    {
        "workspace" => (type, attribute) => attribute == "tags" || (type == "ViewInclude" && attribute is "members" or "excludes"),
        _ => null,
    };

    /// <summary>The findings a binding's tool type turns off (DISL <c>constraints</c>): the C4 types read an element written without an identifier without one.</summary>
    public static IReadOnlySet<string> DisabledFindings(string binding) => binding switch
    {
        "workspace" => new HashSet<string>(StringComparer.Ordinal) { "std.missingId" },
        _ => new HashSet<string>(StringComparer.Ordinal),
    };

    /// <summary>Whether a binding's tool type keeps DISL's default deletion policy, which clears the references to a removed element.</summary>
    public static bool UnsetsReferences(string binding) => binding == "aad";

    private static string Attribute(IdRequest request, string name) =>
        request.Attributes.TryGetValue(name, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";

    private static string? Unprefixed(string? id) => id?[(id.IndexOf(':') + 1)..];
}
