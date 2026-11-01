namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// The attribute types of the Wardley map's tool type that a binding does not carry and FBL section 5.2 reads by, for the
/// <c>wardley-*</c> fixtures, under the type names of <c>wardley-map.fbl</c>: <c>definitions/diagrams/wardley-map.dis</c> in
/// etalii.adp types a position's two numbers <c>Fraction</c> (a number), a size <c>MapSize</c> (two whole numbers), an
/// annotation's number <c>int</c> and a component's <c>inertia</c> <c>bool</c>. Its binding's types are mapped onto that
/// metamodel by the typeMap of the specification's first appendix, which the definition does not declare yet.
/// </summary>
internal static class WardleyTypes
{
    public static string? Of(string type, string attribute) => (type, attribute) switch
    {
        ("Component" or "Anchor" or "Submap", "visibility" or "maturity") => "number",
        ("Component" or "Anchor" or "Submap", "inertia") => "bool",
        ("Evolve" or "PipelineComponent", "maturity") => "number",
        ("Note" or "AnnotationsPosition" or "Accelerator", "visibility" or "maturity") => "number",
        ("Attitude", "visibility1" or "maturity1" or "visibility2" or "maturity2") => "number",
        ("Annotation", "number") => "int",
        ("Size", "width" or "height") => "int",
        _ => null,
    };
}
