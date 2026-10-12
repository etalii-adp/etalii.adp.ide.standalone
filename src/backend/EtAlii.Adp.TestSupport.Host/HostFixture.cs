using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.TestSupport.Host;

/// <summary>
/// One in-process backend host for a test class, in the developer environment, with its project
/// list and problem cache in a scratch folder of its own.
/// </summary>
/// <remarks>
/// <para>
/// Used as an xUnit class fixture: the tests of a class share the host, and each test takes a
/// project folder of its own from <see cref="NewProjectFolder"/>, so no test sees another's
/// files. The project list is shared across the class's tests; a test that asserts on the whole
/// list removes what it adds.
/// </para>
/// </remarks>
public sealed class HostFixture : IDisposable
{
    private readonly TestFolder _appData;
    private readonly WebApplicationFactory<Program> _factory;

    public HostFixture()
    {
        _appData = TestFolder.Create("integration");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // appsettings.developer.json supplies the local-mode credential.
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                // The project list and the problem cache live and die with this host, not in
                // the real user profile the host's own registrations point at.
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(AppDataRoot));
                services.RemoveAll<IProblemStore>();
                services.AddSingleton<IProblemStore>(provider => new ProblemStore(
                    AppDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>()));
            });
        });
    }

    /// <summary>The scratch folder that stands in for the application data folder.</summary>
    public string AppDataRoot => _appData.Path;

    /// <summary>The host's services, for a test that asserts on what the host holds.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>A new, empty project folder under <see cref="AppDataRoot"/>.</summary>
    public string NewProjectFolder(string name = "sample-project")
    {
        var folder = IoPath.Combine(AppDataRoot, $"{Guid.NewGuid():N}", name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A gRPC channel to the host. The caller disposes it.</summary>
    public GrpcChannel CreateChannel()
    {
        var httpClient = _factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _appData.Dispose();
    }
}
