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
        _ => null,
    };

    private static string Attribute(IdRequest request, string name) =>
        request.Attributes.TryGetValue(name, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";

    private static string? Unprefixed(string? id) => id?[(id.IndexOf(':') + 1)..];
}
