using System.Reflection;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>
/// An <see cref="Assembly"/> that serves a chosen set of (real) types under a chosen name. It
/// lets one test assembly stand in for several, so discovery's per-assembly behaviour - the
/// ordinal tie-break on a collision - can be exercised without building extra assemblies.
/// </summary>
public sealed class FakeAssembly(string name, params Type[] types) : Assembly
{
    public override string FullName => $"{name}, Version=1.0.0.0";

    public override AssemblyName GetName() => new(name);

    public override AssemblyName GetName(bool copiedName) => GetName();

    public override Type[] GetTypes() => types;
}
