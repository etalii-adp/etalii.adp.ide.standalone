using System.Xml.Linq;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// No project a tool module reaches carries a web or gRPC hosting package (code-structure FR-054,
/// FR-065; SC-012).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it matters.</b> A module is written against the contracts of the core, not against the
/// server that hosts them. Until the M4 review every module reached ASP.NET Core and
/// <c>Grpc.AspNetCore</c> through the context, hierarchy, projects and sessions projects, which use
/// only the API of gRPC (<c>ServerCallContext</c>, <c>RpcException</c>, <c>Interceptor</c> and the
/// generated service bases) and so need <c>Grpc.Core.Api</c>, and through
/// <c>Microsoft.AspNetCore.Hosting.Abstractions</c>, taken only for <c>IHostApplicationBuilder</c>,
/// which <c>Microsoft.Extensions.Hosting.Abstractions</c> provides. The host project is where
/// hosting belongs, and nothing a module references is the host.
/// </para>
/// <para>
/// <b>Why a guard over the project files.</b> A hosting reference compiles: nothing a module
/// does stops building when one comes back, so only a check over the references themselves sees it.
/// </para>
/// <para>
/// <b>Seen to fail</b> against the tree before the change: it named the context, hierarchy,
/// projects and sessions projects, <c>EtAlii.Adp</c>, the editor and designer projects, the five
/// editor modules and the knowledge designer.
/// </para>
/// </remarks>
public class ModuleHostingPackagesTests
{
    private static string RepositoryRoot { get; } = RepositoryPaths.Root;

    /// <summary>The folders under <c>src/</c> that hold the tool modules, one per kind of tool.</summary>
    private static readonly string[] ModuleFolders = ["diagrams", "designers", "editors"];

    [Fact]
    public void NoProjectAModuleReaches_CarriesAHostingPackage()
    {
        // Arrange.
        var reached = ModuleProjects().SelectMany(Closure).Distinct(StringComparer.Ordinal);

        // Act.
        var offenders = reached
            .SelectMany(project => HostingReferencesOf(project).Select(reference => $"{Relative(project)}: {reference}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert.
        Assert.True(
            offenders.Count == 0,
            "A project a tool module reaches carries a web or gRPC hosting package. Take Grpc.Core.Api "
                + "in place of Grpc.AspNetCore and the Microsoft.AspNetCore.App framework reference, and "
                + "Microsoft.Extensions.Hosting.Abstractions in place of Microsoft.AspNetCore.Hosting.Abstractions; "
                + "hosting belongs in the host project:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The guard's own floor: a sweep that finds no module, or does not follow a reference, passes
    /// over a clean tree and an empty one alike.
    /// </summary>
    [Fact]
    public void TheSweepFollowsTheModulesIntoTheCore()
    {
        // Arrange.
        var modules = ModuleProjects().ToList();

        // Act.
        var reached = modules.SelectMany(Closure).Select(IoPath.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

        // Assert.
        Assert.Contains(modules, module => IoPath.GetFileNameWithoutExtension(module) == "EtAlii.Adp.Diagram.Mindmap");
        Assert.Contains(modules, module => IoPath.GetFileNameWithoutExtension(module) == "EtAlii.Adp.Editor.Json");
        Assert.Contains(modules, module => IoPath.GetFileNameWithoutExtension(module) == "EtAlii.Adp.Designer.Knowledge");
        Assert.Contains("EtAlii.Adp.Sessions", reached);
        Assert.Contains("EtAlii.Adp.Context", reached);
        Assert.Contains("EtAlii.Adp", reached);
    }

    /// <summary>
    /// The production project of every tool module: <c>src/&lt;kind&gt;/&lt;module&gt;/backend/&lt;project&gt;/&lt;project&gt;.csproj</c>.
    /// One level down only, because test fixtures hold project files of their own.
    /// </summary>
    private static IEnumerable<string> ModuleProjects() =>
        ModuleFolders
            .Select(folder => IoPath.Combine(RepositoryRoot, "src", folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateDirectories(folder))
            .Select(module => IoPath.Combine(module, "backend"))
            .Where(Directory.Exists)
            .SelectMany(backend => Directory.EnumerateDirectories(backend))
            .SelectMany(project => Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly))
            .Where(project => !IoPath.GetFileNameWithoutExtension(project).EndsWith(".Tests", StringComparison.Ordinal))
            .Where(project => !IoPath.GetFileNameWithoutExtension(project).Contains(".TestSupport", StringComparison.Ordinal));

    /// <summary>The project and every project it reaches through its project references.</summary>
    private static IEnumerable<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([IoPath.GetFullPath(project)]);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var reference in Items(current, "ProjectReference"))
            {
                pending.Push(IoPath.GetFullPath(IoPath.Combine(IoPath.GetDirectoryName(current)!, reference.Replace('\\', IoPath.DirectorySeparatorChar))));
            }
        }

        return seen;
    }

    /// <summary>The web SDK, the ASP.NET Core framework reference and every ASP.NET Core or gRPC hosting package of a project.</summary>
    private static IEnumerable<string> HostingReferencesOf(string project)
    {
        var sdk = XDocument.Load(project).Root?.Attribute("Sdk")?.Value;
        if (sdk == "Microsoft.NET.Sdk.Web")
        {
            yield return "Sdk=" + sdk;
        }

        foreach (var framework in Items(project, "FrameworkReference").Where(name => name == "Microsoft.AspNetCore.App"))
        {
            yield return "FrameworkReference " + framework;
        }

        foreach (var package in Items(project, "PackageReference").Where(IsHostingPackage))
        {
            yield return "PackageReference " + package;
        }
    }

    private static bool IsHostingPackage(string package) =>
        package.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
        || package.StartsWith("Grpc.AspNetCore", StringComparison.Ordinal);

    private static IEnumerable<string> Items(string project, string item) =>
        XDocument.Load(project)
            .Descendants(item)
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();

    private static string Relative(string path) => IoPath.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
}
