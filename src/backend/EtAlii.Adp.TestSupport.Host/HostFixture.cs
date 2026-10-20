using EtAlii.Adp.Diagram;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using Grpc.Net.Client;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Core;
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
/// <para>
/// A class whose host needs a registration of its own derives a fixture from this one and
/// overrides <see cref="ConfigureServices"/>.
/// </para>
/// <para>
/// The host's pipeline also hands every event on to <see cref="Outside"/>, so a test project that
/// runs flows still hears its own loggers through <c>LogCapture</c>: see <see cref="Outside"/>.
/// </para>
/// </remarks>
[UsedImplicitly] // Instantiated by xUnit for each class that declares IClassFixture<HostFixture>.
public class HostFixture : IDisposable
{
    /// <summary>
    /// What <see cref="Log.Logger"/> was before the first fixture's host ran: in a test project,
    /// <c>LogCapture</c>'s pipeline, which its module initializer installs.
    /// </summary>
    /// <remarks>
    /// A running host points <see cref="Log.Logger"/> at its own pipeline, and a class's
    /// <c>private static readonly ILogger</c> binds to whatever the global is when the class is
    /// first used. A test project whose flows run hosts beside tests that assert on
    /// <c>LogCapture</c> - every module test project since spec 025's T303 - would otherwise lose
    /// the events of each class first used while a flow's host was up, so a presence assertion
    /// fails and an absence assertion passes without having looked. Read in the static
    /// constructor, at the first fixture, because the host's own start replaces the global with
    /// a bootstrap logger before its services are configured. The host's minimum level still
    /// applies: a debug event of a class bound to the host's pipeline does not arrive.
    /// <c>LogCaptureThroughAFlowHostTests</c> in the timeline's test project guards this.
    /// </remarks>
    private static readonly ILogger Outside;

    private readonly TestFolder _appData;
    private readonly WebApplicationFactory<Program> _factory;

    static HostFixture()
    {
        Outside = Log.Logger;
    }

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
                services.AddSingleton<IProjectStore>(new FileProjectStore(AppDataRoot, new FileWriter()));
                services.RemoveAll<IProblemStore>();
                services.AddSingleton<IProblemStore>(provider => new ProblemStore(
                    AppDataRoot,
                    provider.GetRequiredService<DiagramFileRouter>(),
                    provider.GetRequiredService<DiagramValidators>(),
                    provider.GetRequiredService<TimeProvider>()));
                // Program.cs reads its sinks from the services too (ReadFrom.Services).
                services.AddSingleton<ILogEventSink>(new ForwardingLogSink(Outside));
                ConfigureServices(services);
            });
        });
    }

    /// <summary>
    /// Adds a derived fixture's registrations after the standard ones, so a registration made
    /// here replaces or joins what the host registers. Runs when the host is first built.
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>The scratch folder that stands in for the application data folder.</summary>
    private string AppDataRoot => _appData.Path;

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
    public GrpcChannel CreateChannel() => _factory.CreateGrpcChannel();

    public void Dispose()
    {
        _factory.Dispose();
        _appData.Dispose();
    }
}
