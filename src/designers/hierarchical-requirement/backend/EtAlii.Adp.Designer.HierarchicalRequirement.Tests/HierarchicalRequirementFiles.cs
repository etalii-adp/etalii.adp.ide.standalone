using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>The module's folders, found above the test's output folder: its definition, its examples, and the vendored fixtures of its tool type.</summary>
internal static class HierarchicalRequirementFiles
{
    private static readonly Lazy<string> _module = new(() =>
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = IoPath.Combine(folder.FullName, "src", "designers", "hierarchical-requirement");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("The Hierarchical Requirement module's folder was not found above the test's output folder.");
    });

    /// <summary>The vendored definition: the specification, the binding, the companion and their provenance.</summary>
    public static string DefinitionFolder => IoPath.Combine(_module.Value, "definition");

    /// <summary>The module's examples.</summary>
    public static string ExamplesFolder => IoPath.Combine(_module.Value, "examples");

    /// <summary>The tool type's fixtures as the FBL corpus vendors them, from etalii-adp/etalii.adp.</summary>
    public static string FixturesFolder => IoPath.GetFullPath(IoPath.Combine(_module.Value, "..", "..", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance", "etalii.adp", "specifications", "fbl", "fixtures"));
}
