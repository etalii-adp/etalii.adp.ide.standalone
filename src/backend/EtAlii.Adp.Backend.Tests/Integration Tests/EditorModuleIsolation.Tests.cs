using System.Text;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Editor;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
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
public class EditorModuleIsolationTests(EditorModuleIsolationTests.Host host) : IClassFixture<EditorModuleIsolationTests.Host>
{
    private static readonly EditorDefinition Detonating =
        new("boom", "Detonating editor", "A test double that fails on open.", Extensions: [".boom"]);

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    /// <summary>The shared host, with the detonating module riding in beside the real ones.</summary>
    public sealed class Host : HostFixture
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            // The discovered definitions plus one claimant of .boom, and its throwing factory.
            services.RemoveAll<IEditorDefinitionCatalog>();
            services.AddSingleton<IEditorDefinitionCatalog>(provider => new EditorDefinitionCatalog
            {
                All = [.. provider.GetService<IReadOnlyList<EditorDefinition>>() ?? [], Detonating],
            });
            services.AddSingleton<IEditorSessionFactory>(new ThrowingEditorSessionFactory());
        }
    }

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

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "device.boom"), "innocuous text\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "notes.txt"), "still standing\n");
        return projectFolder;
    }

    [Fact]
    public async Task AThrowingEditorModule_CostsItsOwnTabAndNothingElse()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);
        var watchId = ShortGuid.NewShortGuid();

        // Act 1: the broken module's file. Its stream fails - that tab shows an error.
        var boomPath = Path.Of("device.boom");
        using var boomCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = boomPath }, session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await boomCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
            {
            }
        });

        // Act 2: the same connection opens a healthy file afterwards.
        var notesPath = Path.Of("notes.txt");
        using var notesCall = diagramClient.Open(new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = notesPath }, session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await notesCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // Assert: the workspace is unaffected - plain still answers, content and all.
        var add = notesCall.ResponseStream.Current.Add;
        var element = Assert.Single(add.Elements);
        Assert.Equal("editor/plain", element.Type);
        Assert.Equal("still standing\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }
}
