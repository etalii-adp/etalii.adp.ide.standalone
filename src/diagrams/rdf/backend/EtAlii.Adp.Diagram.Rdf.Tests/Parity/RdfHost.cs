using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The module as the host runs it, over a folder of its own: the family's one store, the project's history and
/// the four readings' diagram sessions, for the tests of rdf-disl-fbl task 7 that edit, undo and refuse through it.
/// </summary>
internal sealed class RdfHost : IAsyncDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("rdf-host");
    private readonly ServiceProvider _services;
    private readonly List<IDiagramSession> _sessions = [];

    public RdfHost()
    {
        _services = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddRdf()
            .BuildServiceProvider();
        Store = _services.GetRequiredService<IRdfDocumentStore>();
        History = _services.GetRequiredService<IHistoryStackStore>().Get(Root);
    }

    /// <summary>The folder every file of this host is in.</summary>
    private ProjectRoot Root => ProjectRoot.From(_folder.Path);

    /// <summary>The family's one store.</summary>
    public IRdfDocumentStore Store { get; }

    /// <summary>The project's history.</summary>
    public IHistoryStack History { get; }

    /// <summary>Writes <paramref name="bytes"/> as the file <paramref name="name"/> of the folder, and answers its path.</summary>
    public string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Copies the corpus document at <paramref name="relative"/> (to <c>src/</c>) into the folder, and answers its path.</summary>
    public string Copy(string relative) => Write(Path.GetFileName(relative), File.ReadAllBytes(Path.Combine(RdfCorpus.SourceFolder, relative)));

    /// <summary>The bytes of the file at <paramref name="path"/> now.</summary>
    public static byte[] Bytes(string path) => File.ReadAllBytes(path);

    /// <summary>Opens <paramref name="body"/> under the reading <paramref name="origin"/>, as a diagram's first open does.</summary>
    public IDiagramSession Open(ToolOrigin origin, string body, string? registration = null)
    {
        var session = _services.GetServices<IDiagramSessionFactory>()
            .Single(candidate => candidate.Origin == origin)
            .Open(ShortGuid.NewShortGuid(), Root, body, registration);
        _sessions.Add(session);
        return session;
    }

    /// <summary>The ids <paramref name="session"/> draws for its document as it is now.</summary>
    public static async Task<IReadOnlyList<DiagramElement>> DrawnAsync(IDiagramSession session, CancellationToken cancellationToken) =>
        [.. (await session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(add => add.Elements)];

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        await _services.DisposeAsync();
        _folder.Dispose();
    }
}
