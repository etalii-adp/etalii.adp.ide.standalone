using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Planning;
using Serilog;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// Keeps relations pointing at a table that was renamed (knowledge-designer Requirement 5.8): every
/// knowledge file in the project whose relation names the renamed file, or a file in the renamed
/// folder, names it by its new path from the moment it has it.
/// </summary>
/// <remarks>
/// Part of the rename's own step. An undo of the rename is a rename back, which comes through
/// here again and puts each relation back as it was, so nothing is kept between the two.
/// </remarks>
internal sealed class KnowledgeTargetRename(IKnowledgeDocumentStore documents, IFileWriter writer) : IEntryRenameFollower
{
    private static readonly ILogger _logger = Log.ForContext<KnowledgeTargetRename>();

    public async Task RenamedAsync(string rootPath, string fromPath, string toPath, CancellationToken cancellationToken)
    {
        foreach (var path in KnowledgeProject.Files(rootPath))
        {
            if (KnowledgeDocumentStore.Read(path).Body is not { ReadOnlyReason.Length: 0 } body)
            {
                continue;
            }

            List<ModelChange> changes = [];
            foreach (var property in body.Table.Properties.Where(property => property is { ValueType: "relation", TargetFile: { Length: > 0 } and not KnowledgeRelations.Self }))
            {
                if (Now(KnowledgeRelations.TargetPath(path, property.TargetFile), fromPath, toPath) is { } target)
                {
                    changes.Add(new ModelChange.Set(property.Id, new Dictionary<string, object?> { ["targetFile"] = KnowledgeRelations.TargetName(path, target) }));
                }
            }

            if (changes.Count == 0)
            {
                continue;
            }

            (byte[]? after, string refusal) = body.Change(changes);
            if (after is null)
            {
                _logger.Warning("The relations of {Path} do not follow the rename of {FromPath}: {Refusal}", path, fromPath, refusal);
                continue;
            }

            await writer.SaveAsync(path, after, cancellationToken);
            // Not with the request's token: the file is written, so the store reads it whatever
            // becomes of the request.
            await documents.ReloadAsync(path, CancellationToken.None);
        }
    }

    /// <summary>Where a path is after the rename, or null when the rename did not move it.</summary>
    private static string? Now(string path, string fromPath, string toPath)
    {
        if (string.Equals(path, fromPath, StringComparison.OrdinalIgnoreCase))
        {
            return toPath;
        }

        var folder = Path.TrimEndingDirectorySeparator(fromPath) + Path.DirectorySeparatorChar;
        return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? Path.Combine(toPath, path[folder.Length..]) : null;
    }
}
