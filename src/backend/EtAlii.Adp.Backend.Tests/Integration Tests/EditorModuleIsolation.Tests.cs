using System.Text;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Projects;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Non-Functional isolation requirement, verified the way task 7.4 asks: a test-double
/// editor module whose session throws is deployed beside the real ones, and its failure
/// costs exactly its own stream - the connection, the other editors and the host all keep
/// working. An optional, additive family must not be able to take the workspace down.
/// </summary>
public class EditorModuleIsolationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly EditorDefinition Detonating =
        new("boom", "Detonating editor", "A test double that fails on open.", Extensions: [".boom"]);

    private sealed class ThrowingEditorSession : IEditorSession
    {
        public string Content => throw new InvalidOperationException("This module is broken on purpose.");

        // The event is part of the contract; a throwing double still declares it.
        public event EventHandler<EditorContentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingEditorSessionFactory : IEditorSessionFactory
    {
        public string EditorId => "boom";

        public IEditorSession Open(ShortGuid watchId, string rootPath, string filePath) => new ThrowingEditorSession();
    }

    public EditorModuleIsolationTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);
        File.WriteAllText(IoPath.Combine(_projectFolder, "device.boom"), "innocuous text\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.txt"), "still standing\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<Problems.IProblemStore>();
                services.AddSingleton<Problems.IProblemStore>(provider => new Problems.ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));

                // The detonating module rides in beside the real ones: the discovered
                // definitions plus one claimant of .boom, and its throwing factory.
                services.RemoveAll<IEditorDefinitionCatalog>();
                services.AddSingleton<IEditorDefinitionCatalog>(provider => new EditorDefinitionCatalog
                {
                    All = [.. provider.GetService<IReadOnlyList<EditorDefinition>>() ?? [], Detonating],
                });
                services.AddSingleton<IEditorSessionFactory>(new ThrowingEditorSessionFactory());
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Fact]
    public async Task AThrowingEditorModule_CostsItsOwnTabAndNothingElse()
    {
        // Arrange.
        using var channel = _factory.CreateGrpcChannel();
        var headers = await channel.SignInAsync(TestContext.Current.CancellationToken);
        var projectId = await channel.AddProjectAsync(headers, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        var watchId = ShortGuid.NewShortGuid();

        // Act 1: the broken module's file. Its stream fails - that tab shows an error.
        var boomPath = Path.Of("device.boom");
        using var boomCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = boomPath }, headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await boomCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
            {
            }
        });

        // Act 2: the same connection opens a healthy file afterwards.
        var notesPath = Path.Of("notes.txt");
        using var notesCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = projectId, WatchId = watchId, Path = notesPath }, headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await notesCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // Assert: the workspace is unaffected - plain still answers, content and all.
        var add = notesCall.ResponseStream.Current.Add;
        var element = Assert.Single(add.Elements);
        Assert.Equal("editor/plain", element.Type);
        Assert.Equal("still standing\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }
}
