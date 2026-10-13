using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Repository.Tests;

/// <summary>
/// The release build installs the client's packages from the npm workspace root, so a dependency a
/// module declares in its own <c>package.json</c> is installed too.
/// </summary>
/// <remarks>
/// <c>npm ci</c> run inside <c>src/client/</c> installs that one workspace. The JSON editor's
/// language service is declared by <c>src/editors/json/client/package.json</c> alone, so on the
/// merge of #203 the release job's <c>npm run build</c> could not find it while every gate stayed
/// green: the gates install from <c>src/</c>, and only <c>dotnet publish</c> runs this target.
/// </remarks>
public class ReleaseClientInstallTests
{
    private static string ServiceProject { get; } = IoPath.Combine(RepositoryPaths.Root, "src", "backend", "EtAlii.Adp.Backend.Service", "EtAlii.Adp.Backend.Service.csproj");

    [Fact]
    public void ThePublishedClient_IsInstalledFromTheWorkspaceRoot()
    {
        // Arrange.
        var project = XDocument.Load(ServiceProject);
        var install = project.Descendants()
            .Where(element => element.Name.LocalName == "Exec")
            .SingleOrDefault(element => (string?)element.Attribute("Command") == "npm ci");
        Assert.NotNull(install);

        // Act: the folder the install runs in, as MSBuild would resolve it on any platform.
        var workingDirectory = ((string?)install.Attribute("WorkingDirectory") ?? "")
            .Replace("$(MSBuildThisFileDirectory)", IoPath.GetDirectoryName(ServiceProject) + IoPath.DirectorySeparatorChar, StringComparison.Ordinal)
            .Replace('\\', IoPath.DirectorySeparatorChar);
        var manifest = IoPath.Combine(IoPath.GetFullPath(workingDirectory), "package.json");

        // Assert.
        Assert.True(File.Exists(manifest), $"`npm ci` runs in {workingDirectory}, which holds no package.json.");
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.True(
            json.RootElement.TryGetProperty("workspaces", out _),
            $"`npm ci` runs in {IoPath.GetDirectoryName(manifest)}, which is not the npm workspace root (src/): a module's own dependencies would not be installed for the release build.");
    }
}
