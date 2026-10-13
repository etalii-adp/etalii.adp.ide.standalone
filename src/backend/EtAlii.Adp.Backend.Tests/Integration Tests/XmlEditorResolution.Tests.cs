using System.Text;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Editor;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Where an XML file lands in the real host (xml-editor Requirements 2.2, 2.3, 2.5): a file no
/// diagram or designer claims opens in the XML editor with nothing registered; the knowledge
/// designer's <c>cities.xml</c> stays the designer's, and "Open as text" on it opens the XML
/// editor; and adding the editor's claims reports no conflict.
/// </summary>
public class XmlEditorResolutionTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "notes.xml"), "<notes/>\n");

        // The knowledge designer's own example, with its registration.
        var knowledge = RepositoryPaths.Under("src", "examples", "designers", "knowledge", "xml");
        File.Copy(IoPath.Combine(knowledge, "cities.adp"), IoPath.Combine(projectFolder, "cities.adp"));
        File.Copy(IoPath.Combine(knowledge, "cities.xml"), IoPath.Combine(projectFolder, "cities.xml"));
        return projectFolder;
    }

    [Fact]
    public async Task AnXmlFileNothingElseClaims_OpensInTheXmlEditor()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        using var call = client.Open(Request(session.ProjectId, "notes.xml", ""), session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // Assert.
        var element = Assert.Single(call.ResponseStream.Current.Add.Elements);
        Assert.Equal("editor/xml", element.Type);
        Assert.Equal("<notes/>\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    [Fact]
    public async Task TheKnowledgeDesignersCities_IsNotOpenedAsText()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act: opened as a document, the way activating it in the hierarchy asks.
        using var call = client.Open(Request(session.ProjectId, "cities.xml", ""), session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        var refusal = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
            {
                Assert.Fail($"cities.xml opened on the diagram stream as {call.ResponseStream.Current.Add?.Elements.FirstOrDefault()?.Type}.");
            }
        });

        // Assert: the designer's document is refused on this stream by its designer's name, as
        // it was before the XML editor claimed .xml - it opens in its own table instead.
        Assert.Contains("etalii/knowledge", refusal.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsText_OnTheKnowledgeDesignersCities_OpensTheXmlEditor()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act: "*" is the "Open as text" tab's request - whichever editor the resolver answers.
        using var call = client.Open(Request(session.ProjectId, "cities.xml", "*"), session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // Assert.
        var element = Assert.Single(call.ResponseStream.Current.Add.Elements);
        Assert.Equal("editor/xml", element.Type);
    }

    [Fact]
    public void TheDeployedEditors_ClaimEachExtensionOnce()
    {
        // Arrange.
        using var _ = host.CreateChannel();
        var definitions = host.Services.GetRequiredService<IEditorDefinitionCatalog>().All;

        // Act.
        var shared = definitions
            .SelectMany(definition => definition.Extensions.Select(extension => (extension, definition.Id)))
            .GroupBy(claim => claim.extension, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(claim => claim.Id))}")
            .ToArray();

        // Assert: the XML editor is deployed, and no extension is claimed twice - which is what
        // the resolver would report at startup as a conflict.
        Assert.Contains(definitions, definition => definition.Id == "xml");
        Assert.True(shared.Length == 0, $"claimed more than once: {string.Join("; ", shared)}");
    }

    private static OpenDiagramRequest Request(Documents.Wire.ShortGuid projectId, string file, string editorId) =>
        new() { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = Path.Of(file), EditorId = editorId };
}
