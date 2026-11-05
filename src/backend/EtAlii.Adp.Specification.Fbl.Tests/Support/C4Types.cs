namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The attribute types of the C4 tool types that a binding does not carry and FBL section 5.2 reads by, for the
/// <c>structurizr-*</c> fixtures: the separations an <c>autoLayout</c> statement gives, which every
/// <c>definitions/diagrams/c4-*.dis</c> in etalii.adp types <c>int</c> on the diagram (where <c>ViewInclude</c> is mapped).
/// </summary>
internal static class C4Types
{
    public static string? Of(string type, string attribute) => (type, attribute) switch
    {
        ("ViewInclude", "rankSeparation" or "nodeSeparation") => "int",
        _ => null,
    };
}
