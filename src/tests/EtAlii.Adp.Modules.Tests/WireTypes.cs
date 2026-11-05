using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Modules.Tests;

/// <summary>
/// Every element and relation type string a diagram module puts on the wire, read by reflection from
/// the modules' own constants - the list <c>src/fixtures/cross-tier/element-types.json</c> is written
/// from (backend-centralization R12.1) and <see cref="DrawnConnectionsTests"/> checks the canvases by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is kept by hand.</b> A wire type is a public string constant shaped
/// <c>vendor/type+kind</c> in an application assembly. Its module is the folder under
/// <c>src/diagrams</c> whose <c>backend</c> holds the declaring assembly's project, and it is a
/// relation exactly when the constant carries <see cref="RelationAttribute"/>; every other one is an
/// element. So a module adding a type adds it here by declaring it, and says how it is drawn where it
/// declares it.
/// </para>
/// <para>
/// A constant of that shape outside every module folder gets the module <c>""</c>, which
/// <see cref="ElementTypesTests"/> refuses rather than leaving it out of the fixture.
/// </para>
/// </remarks>
internal static partial class WireTypes
{
    /// <summary>One wire-type constant, where it is declared, and whether it is drawn between two elements.</summary>
    internal sealed record WireType(string Module, string Value, bool IsRelation, string Where);

    private static readonly Lazy<IReadOnlyList<WireType>> _all = new(Find);

    /// <summary>Every wire-type constant of every application assembly that is not a test assembly.</summary>
    internal static IReadOnlyList<WireType> All => _all.Value;

    /// <summary>The application assemblies that are not test assemblies, which is where a module's types live.</summary>
    internal static IEnumerable<Assembly> Assemblies() =>
        ApplicationAssemblies.Find().Where(assembly => !(assembly.GetName().Name ?? "").EndsWith(".Tests", StringComparison.Ordinal));

    /// <summary>
    /// The folder under <c>src/diagrams</c> whose <c>backend</c> holds <paramref name="assembly"/>'s
    /// project, or <c>""</c> when none does - a core assembly.
    /// </summary>
    internal static string ModuleOf(Assembly assembly)
    {
        var diagrams = IoPath.Combine(RepositoryPaths.Root, "src", "diagrams");
        var name = assembly.GetName().Name ?? "";
        return Directory.EnumerateDirectories(diagrams)
            .Select(folder => IoPath.GetFileName(folder))
            .FirstOrDefault(folder => Directory.Exists(IoPath.Combine(diagrams, folder, "backend", name))) ?? "";
    }

    /// <summary>Whether <paramref name="field"/> is a public string constant shaped like a wire type.</summary>
    internal static bool IsWireType(FieldInfo field) =>
        field is { IsLiteral: true, IsPublic: true, IsStatic: true } &&
        field.FieldType == typeof(string) &&
        Shape().IsMatch((string)field.GetRawConstantValue()!);

    private static List<WireType> Find() =>
    [
        .. Assemblies().SelectMany(assembly =>
        {
            var module = ModuleOf(assembly);
            return assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(IsWireType)
                    .Select(field => new WireType(
                        module,
                        (string)field.GetRawConstantValue()!,
                        field.IsDefined(typeof(RelationAttribute), inherit: false),
                        $"{type.FullName}.{field.Name}")));
        }),
    ];

    /// <summary>The shape every module's element type takes on the wire: <c>vendor/type+kind</c>.</summary>
    [GeneratedRegex(@"^[a-z0-9-]+/[a-z0-9-]+\+[a-z0-9-]+$")]
    private static partial Regex Shape();
}
