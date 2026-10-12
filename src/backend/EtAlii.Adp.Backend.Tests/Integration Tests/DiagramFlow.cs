using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The steps the document-backed diagram flows share: open a diagram and read its baseline, find
/// the project's entries, select an element the way a canvas does, and run a project action.
/// </summary>
/// <remarks>
/// The RDF, OWL and Databricks flows each carried these word for word; what differs between them is
/// what they open and what they assert, which stays in each class.
/// </remarks>
internal static class DiagramFlow
{
    /// <summary>Opens <paramref name="fileName"/> on a watch of its own and returns its baseline; empty when none came before the deadline.</summary>
    public static async Task<IReadOnlyList<Element>> BaselineAsync(FlowSession session, string fileName)
    {
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = diagramClient.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = PathOf(fileName) },
            session.Headers, cancellationToken: cts.Token);
        return await ReadAddAsync(call.ResponseStream, cts.Token);
    }

    /// <summary>The next delta that adds or replaces elements; an empty list when none came before the deadline.</summary>
    public static async Task<IReadOnlyList<Element>> ReadAddAsync(
        IAsyncStreamReader<Delta> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. stream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline; the caller's assertion reports the empty baseline.
        }
        catch (OperationCanceledException)
        {
            // Same story, thrown the other way.
        }

        return [];
    }

    /// <summary>
    /// Every entry of the project, by name - one nested level deep, because a registration is a
    /// child of the subject it names.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, Documents.Wire.ShortGuid>> EntriesAsync(FlowSession session, ShortGuid watchId)
    {
        var response = await session.Hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        var byName = response.Entries.Entries_.ToDictionary(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);
        foreach (var parent in response.Entries.Entries_.Where(entry => entry.HasChildren && entry.Kind != EntryKind.Folder))
        {
            var children = await session.Hierarchy.ListEntriesAsync(
                new ListEntriesRequest { ProjectId = session.ProjectId, WatchId = watchId, FolderId = parent.Id },
                session.Headers, cancellationToken: TestContext.Current.CancellationToken);
            foreach (var child in children.Entries.Entries_)
            {
                byName[child.Name] = child.Id;
            }
        }

        return byName;
    }

    /// <summary>The canvas's own selection shape: the <c>.adp</c> file, then the element as its child.</summary>
    public static ContextSelection ElementChain(Documents.Wire.ShortGuid entryId, string elementId) =>
        new()
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
            },
        };

    /// <summary>Runs a project-scoped action, such as undo, on the given watch.</summary>
    public static Task<ExecuteActionResponse> ExecuteProjectActionAsync(FlowSession session, ShortGuid watchId, string actionId) =>
        session.Context.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Source = new ContextSource { Project = new Google.Protobuf.WellKnownTypes.Empty() },
                InteractionId = ShortGuid.NewShortGuid(),
                ActionId = actionId,
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    /// <summary>A project-relative path of one segment.</summary>
    public static Path PathOf(string fileName)
    {
        var path = Path.Of(fileName);
        return path;
    }
}
