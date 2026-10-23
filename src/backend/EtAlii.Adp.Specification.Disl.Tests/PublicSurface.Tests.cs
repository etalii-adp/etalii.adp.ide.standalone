using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The public types of the three specification projects, CEL, FBL and DISL, are the ones another
/// project uses (spec 025, FR-082 and SC-022): each is named in the C# of a project other than itself
/// and its own tests, or a public signature of such a type exposes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it holds, and what it does not.</b> A new public type that no other project names, and that
/// nothing public hands out, fails here; make it <c>internal</c>, and give its own tests the
/// <c>InternalsVisibleTo</c> they already have. Nested types follow their parent and are not checked
/// apart from it: they are the cases of a public result or request (<c>HostAction</c>,
/// <c>ModelChange</c>), which a host matches on as a whole. Members are not checked: a public member
/// keeps the types its signature names public, whether or not another project reads that member.
/// </para>
/// <para>
/// <b>Why it reads the sources and not the other assemblies.</b> This project references the three
/// libraries and no module, and a module's build output is only as current as its last build; the
/// sources are what every run reads alike. A name is a whole word in code, comment lines left out,
/// so a type another project names under the same name keeps one here public: the guard can be
/// lenient, never wrong in the other direction.
/// </para>
/// <para>
/// <b>Seen to fail</b> (spec 025, T226): making <c>CelStrings</c> public again failed this test naming
/// exactly <c>EtAlii.Adp.Specification.Cel.CelStrings</c>; restoring <c>internal</c> restored green.
/// </para>
/// </remarks>
public class PublicSurfaceTests
{
    private static readonly Regex Word = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", "Fixtures", "fixtures"];

    [Fact]
    public void EveryPublicTypeIsUsedByAnotherProjectOrExposedByOneThatIs()
    {
        // Arrange.
        Assembly[] libraries = [typeof(CelProgram).Assembly, typeof(FblModel).Assembly, typeof(DislSpecification).Assembly];
        var source = Path.Combine(RepositoryRoot(), "src");
        var files = SourceFiles(source).ToList();
        var surface = libraries.SelectMany(library => library.GetExportedTypes()).ToHashSet();

        // Act.
        var reached = new HashSet<Type>();
        foreach (var library in libraries)
        {
            var project = library.GetName().Name!;
            var own = Path.Combine(source, "backend", project) + Path.DirectorySeparatorChar;
            var ownTests = Path.Combine(source, "backend", project + ".Tests") + Path.DirectorySeparatorChar;
            var words = Words(files.Where(file => !file.StartsWith(own, StringComparison.Ordinal) && !file.StartsWith(ownTests, StringComparison.Ordinal)));
            reached.UnionWith(library.GetExportedTypes().Where(type => !type.IsNested && words.Contains(SimpleName(type))));
        }

        var pending = new Queue<Type>(reached);
        while (pending.TryDequeue(out var type))
        {
            foreach (var exposed in Exposed(type).Where(surface.Contains))
            {
                var outermost = exposed;
                while (outermost.DeclaringType is { } parent) outermost = parent;
                if (reached.Add(outermost)) pending.Enqueue(outermost);
            }
        }

        var unused = surface.Where(type => !type.IsNested && !reached.Contains(type)).Select(type => type.FullName).Order(StringComparer.Ordinal).ToList();

        // Assert.
        Assert.True(unused.Count == 0, "Public, yet no other project names it and no public signature of a type one does exposes it; make it internal:\n" + string.Join("\n", unused));
    }

    /// <summary>The types a public or protected signature of <paramref name="type"/> or of its public nested types names.</summary>
    private static IEnumerable<Type> Exposed(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var named = new List<Type>();
        if (type.BaseType is { } baseType) named.Add(baseType);
        named.AddRange(type.GetInterfaces());
        foreach (var member in type.GetMembers(declared))
        {
            switch (member)
            {
                case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                    named.Add(field.FieldType);
                    break;
                case PropertyInfo property when property.GetAccessors(nonPublic: true).Any(Visible):
                    named.Add(property.PropertyType);
                    named.AddRange(property.GetIndexParameters().Select(parameter => parameter.ParameterType));
                    break;
                case MethodBase method when Visible(method):
                    if (method is MethodInfo returning) named.Add(returning.ReturnType);
                    named.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
                    break;
                case EventInfo { AddMethod: { } add } @event when Visible(add):
                    named.Add(@event.EventHandlerType!);
                    break;
                case Type nested when nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem:
                    named.Add(nested);
                    named.AddRange(Exposed(nested));
                    break;
            }
        }

        return named.SelectMany(Parts);
    }

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    /// <summary><paramref name="type"/> and every type it is built from: an element type, a generic argument.</summary>
    private static IEnumerable<Type> Parts(Type type)
    {
        while (true)
        {
            if (!type.HasElementType)
            {
                return type is { IsGenericType: true, IsGenericTypeDefinition: false }
                    ? Parts(type.GetGenericTypeDefinition()).Concat(type.GetGenericArguments().SelectMany(Parts))
                    : [type];
            }

            type = type.GetElementType()!;
        }
    }

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static HashSet<string> Words(IEnumerable<string> files)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match word in Word.Matches(line)) words.Add(word.Value);
            }
        }

        return words;
    }

    private static IEnumerable<string> SourceFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs")) yield return file;
        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(child))) continue;
            foreach (var file in SourceFiles(child)) yield return file;
        }
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "src", "backend", "EtAlii.Adp.slnx"))) return folder.FullName;
        }

        throw new InvalidOperationException("The repository root (the folder holding src/backend/EtAlii.Adp.slnx) was not found above the test assembly.");
    }
}
