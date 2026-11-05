using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>The module's folders, found through <see cref="RepositoryPaths"/>: its definition, its examples, and the vendored fixtures of its tool type.</summary>
internal static class HierarchicalRequirementFiles
{
    private static string Module => RepositoryPaths.Under("src", "designers", "hierarchical-requirement");

    /// <summary>The vendored definition: the specification, the binding, the companion and their provenance.</summary>
    public static string DefinitionFolder => IoPath.Combine(Module, "definition");

    /// <summary>The module's examples.</summary>
    public static string ExamplesFolder => IoPath.Combine(Module, "examples");

    /// <summary>The tool type's fixtures as the FBL corpus vendors them, from etalii-adp/etalii.adp.</summary>
    public static string FixturesFolder => RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance", "etalii.adp", "specifications", "fbl", "fixtures");
}
