using EtAlii.Adp.Hierarchy.Wire;
using Grpc.Core;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Finds an entry the way the tree now shows it (adp-file-nesting Requirement 3): a diagram
/// registration is a child of its subject file, not a root sibling, so a lookup that only reads
/// the root listing no longer sees it. This walks the root and then one level under every entry
/// that reports children - the same walk a user's eye makes.
/// </summary>
internal static class NestedEntryLookup
{
    public static async Task<Documents.Wire.ShortGuid> EntryIdOfAsync(
        HierarchyService.HierarchyServiceClient hierarchy,
        Documents.Wire.ShortGuid projectId,
        Documents.Wire.ShortGuid watchId,
        Metadata headers,
        string name)
    {
        var root = await hierarchy.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = watchId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        var hit = root.Entries.Entries_.SingleOrDefault(entry => entry.Name == name);
        if (hit is not null)
        {
            return hit.Id;
        }

        foreach (var parent in root.Entries.Entries_.Where(entry => entry.HasChildren))
        {
            var children = await hierarchy.ListEntriesAsync(
                new ListEntriesRequest { ProjectId = projectId, WatchId = watchId, FolderId = parent.Id },
                headers,
                cancellationToken: TestContext.Current.CancellationToken);

            var nested = children.Entries.Entries_.SingleOrDefault(entry => entry.Name == name);
            if (nested is not null)
            {
                return nested.Id;
            }
        }

        throw new InvalidOperationException($"Entry '{name}' was found neither at the root nor one level down.");
    }
}
