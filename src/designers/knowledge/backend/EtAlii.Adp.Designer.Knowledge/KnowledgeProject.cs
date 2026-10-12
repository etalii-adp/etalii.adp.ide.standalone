using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The knowledge files of a project, for what one of them has to know of the others: which name it
/// by a relation (knowledge-designer Requirement 5.8).
/// </summary>
internal static class KnowledgeProject
{
    /// <summary>What a knowledge file says of itself in each of its three formats: a file without it is not read.</summary>
    private const string Mark = "etalii/knowledge";

    private static readonly string[] _extensions = [".yaml", ".yml", ".json", ".xml"];

    /// <summary>The knowledge file an entry of the explorer stands for: the file itself, or the one its registration names; null when it is neither.</summary>
    public static string? BodyOf(string path) => KnowledgeContextActionProvider.BodyOf(path);

    /// <summary>The files under a folder that say they are knowledge files, folders of tools' own left out.</summary>
    public static IEnumerable<string> Files(string folder)
    {
        IEnumerable<string> files;
        IEnumerable<string> folders;
        try
        {
            files = [.. Directory.EnumerateFiles(folder)];
            folders = [.. Directory.EnumerateDirectories(folder)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files.Where(file => _extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && Says(file)))
        {
            yield return file;
        }

        foreach (var inner in folders.Where(inner => Path.GetFileName(inner) is not ['.', ..] and not "node_modules" and not "bin" and not "obj").SelectMany(Files))
        {
            yield return inner;
        }
    }

    /// <summary>
    /// How many rows of the project's other knowledge files relate to any of the given rows of one
    /// file: what deleting those rows would leave pointing at nothing.
    /// </summary>
    public static int RowsRelatingTo(string rootPath, string bodyPath, IReadOnlyCollection<string> rowIds)
    {
        var count = 0;
        var target = Path.GetFullPath(bodyPath);
        foreach (var path in Files(rootPath).Where(path => !string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase)))
        {
            if (KnowledgeDocumentStore.Read(path).Body is not { } body)
            {
                continue;
            }

            // The side of a two-way relation that holds no values of its own names nothing.
            var naming = body.Table.Properties
                .Where(property => property is { ValueType: "relation", IsComputed: false, TargetFile: { Length: > 0 } and not KnowledgeRelations.Self }
                                   && string.Equals(KnowledgeRelations.TargetPath(path, property.TargetFile), target, StringComparison.OrdinalIgnoreCase))
                .Select(property => property.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (naming.Count > 0)
            {
                count += body.Table.Rows.Count(row => row.Cells.Any(cell => cell.Key == "rows" && naming.Contains(cell.PropertyId) && cell.Values.Any(rowIds.Contains)));
            }
        }

        return count;
    }

    private static bool Says(string file)
    {
        try
        {
            return SharedDocumentReader.ReadAllText(file).Contains(Mark, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
