using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The parity transcript's corpus, each document opened as the transcript opens it, with today's providers and the
/// hand-written ones they replaced side by side over one store (wardley-map-disl-fbl task 6).
/// </summary>
internal static class WardleyCorpus
{
    /// <summary>Every document of the corpus, by its <c>src/</c>-relative name.</summary>
    public static TheoryData<string> Names() => [.. new WardleyTranscript().Corpus().Select(document => document.Name)];

    /// <summary>
    /// The document <paramref name="name"/> opened at <c>maps/&lt;name&gt;</c> in a scratch project whose root holds
    /// <c>at-root.owm</c> and whose <c>maps/</c> folder holds <c>beside.owm</c>, with its identities file beside it.
    /// </summary>
    public static async Task<WardleyCorpusSession> OpenAsync(string name, CancellationToken cancellationToken)
    {
        var transcript = new WardleyTranscript();
        var document = transcript.Corpus().Single(candidate => candidate.Name == name);
        var folder = TestFolder.Create("wardley-parity");
        var maps = folder.Combine("maps");
        Directory.CreateDirectory(maps);
        await File.WriteAllTextAsync(folder.Combine("at-root.owm"), "title At the root\n", cancellationToken);
        await File.WriteAllTextAsync(IoPath.Combine(maps, "beside.owm"), "title Beside the map\n", cancellationToken);

        var body = IoPath.Combine(maps, IoPath.GetFileName(name));
        await File.WriteAllBytesAsync(body, await document.Read(cancellationToken), cancellationToken);
        var sidecar = WardleyIdentities.PathFor(IoPath.Combine(transcript.SourceFolder, name));
        if (File.Exists(sidecar))
        {
            File.Copy(sidecar, WardleyIdentities.PathFor(body));
        }

        var session = new WardleyCorpusSession(folder, body);
        await session.Store.GetOrLoadAsync(body, cancellationToken);
        return session;
    }
}

/// <summary>One document of the corpus, open, with both pairs of providers over its store.</summary>
internal sealed class WardleyCorpusSession : IDisposable
{
    private const string Unknown = "parity:unknown";

    private readonly TestFolder _folder;
    private readonly ServiceProvider _services;

    public WardleyCorpusSession(TestFolder folder, string body)
    {
        _folder = folder;
        Body = body;
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddWardleyMap().BuildServiceProvider();
        Store = _services.GetRequiredService<IWardleyDocumentStore>();
        var history = _services.GetRequiredService<IHistoryStackStore>();
        Actions = new WardleyContextActionProvider(history, Store);
        Properties = new WardleyContextPropertyProvider(history, Store);
        HandWrittenActions = new HandWrittenWardleyActionProvider(history, Store);
        HandWrittenProperties = new HandWrittenWardleyPropertyProvider(history, Store);
        Dispatcher = _services.GetRequiredService<ICommandDispatcher>();
    }

    public string Body { get; }

    /// <summary>The scratch project's root, which a validation request carries.</summary>
    public string Folder => _folder.Path;

    public IWardleyDocumentStore Store { get; }

    public WardleyContextActionProvider Actions { get; }

    public WardleyContextPropertyProvider Properties { get; }

    public HandWrittenWardleyActionProvider HandWrittenActions { get; }

    public HandWrittenWardleyPropertyProvider HandWrittenProperties { get; }

    /// <summary>The dispatcher the commands of this session run through, without a history stack.</summary>
    public ICommandDispatcher Dispatcher { get; }

    /// <summary>What the transcript records menus and rows for: every id the store holds, the empty canvas and an unknown id.</summary>
    public IReadOnlyList<string> Targets => [.. Store.Identities(Body).Select(entry => entry.Id).Distinct(StringComparer.Ordinal), "", Unknown];

    public ContextTarget Target(string elementId) =>
        ContextTarget.ForElement(Body, elementId, ProjectRoot.From(_folder.Path), origin: Diagram.WardleyMap.Origin);

    /// <summary>Runs <paramref name="read"/> with the body's file marked read-only, as a file that cannot be written is.</summary>
    public async Task<T> ReadOnlyAsync<T>(Func<Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        File.SetAttributes(Body, File.GetAttributes(Body) | FileAttributes.ReadOnly);
        try
        {
            return await read();
        }
        finally
        {
            File.SetAttributes(Body, File.GetAttributes(Body) & ~FileAttributes.ReadOnly);
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _folder.Dispose();
    }
}
