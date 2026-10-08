using System.Reflection;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// An <see cref="Assembly"/> that serves a chosen set of (real) types under a chosen name. It
/// lets one test assembly stand in for several, so discovery's per-assembly behaviour - the
/// ordinal tie-break on a collision, a type-load failure, an enumeration failure - can be
/// exercised without building extra assemblies.
/// </summary>
public sealed class FakeAssembly : Assembly
{
    private readonly string _name;
    private readonly Func<Type[]> _getTypes;

    public FakeAssembly(string name, params Type[] types)
        : this(name, () => types)
    {
    }

    private FakeAssembly(string name, Func<Type[]> getTypes)
    {
        _name = name;
        _getTypes = getTypes;
    }

    public override string FullName => $"{_name}, Version=1.0.0.0";

    public override AssemblyName GetName() => new(_name);

    public override AssemblyName GetName(bool copiedName) => GetName();

    public override Type[] GetTypes() => _getTypes();

    /// <summary>An assembly whose enumeration fails part-way, the way a missing dependency makes it fail.</summary>
    public static FakeAssembly PartiallyLoadable(string name, Type[] loaded, params Exception[] failures)
        => new(name, () => throw new ReflectionTypeLoadException(
            loaded.Concat(new Type?[] { null }).ToArray(),
            failures));

    /// <summary>An assembly whose enumeration fails outright.</summary>
    public static FakeAssembly Broken(string name, Exception failure)
        => new(name, () => throw failure);
}
