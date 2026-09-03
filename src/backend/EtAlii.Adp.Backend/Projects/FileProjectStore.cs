using System.Text.Json;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Projects;

/// <summary>
/// Per-user project list persisted as a plain JSON file at
/// {appDataRoot}/EtAlii.Adp/users/{userId}/projects.json, per tech.md's
/// file-based (no database) storage philosophy. ProjectRecord (and its
/// nested PathRecord) is serialized directly - no separate DTO - relying on
/// ShortGuid's own JsonConverter (ShortGuidJsonConverter) for its Id.
/// </summary>
public sealed class FileProjectStore : IProjectStore
{
    private static readonly ILogger _logger = Log.ForContext<FileProjectStore>();

    private readonly string _appDataRoot;

    public FileProjectStore(string appDataRoot)
    {
        _appDataRoot = appDataRoot;
    }

    public IReadOnlyList<ProjectRecord> List(ShortGuid userId) => Read(userId);

    public ProjectRecord Add(ShortGuid userId, string name, PathRecord path)
    {
        var folderPath = path.Segments.AbsolutePath();
        if (!Directory.Exists(folderPath))
        {
            throw new InvalidProjectPathException($"Folder not found: {folderPath}");
        }

        var projects = Read(userId).ToList();
        var resolvedName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(folderPath).Name : name.Trim();
        var record = new ProjectRecord(ShortGuid.NewShortGuid(), resolvedName, path);
        projects.Add(record);
        Write(userId, projects);
        return record;
    }

    public void Remove(ShortGuid userId, ShortGuid projectId)
    {
        var projects = Read(userId).Where(p => p.Id != projectId).ToList();
        Write(userId, projects);
    }

    private string GetFilePath(ShortGuid userId) =>
        IoPath.Combine(_appDataRoot, "EtAlii.Adp", "users", userId.ToString(), "projects.json");

    private IReadOnlyList<ProjectRecord> Read(ShortGuid userId)
    {
        var filePath = GetFilePath(userId);
        if (!File.Exists(filePath))
        {
            // Ordinary for a user who has not added a project yet, so not worth a warning.
            _logger.Verbose("No project list yet for {UserId} at {StorePath}", userId, filePath);
            return Array.Empty<ProjectRecord>();
        }

        var json = File.ReadAllText(filePath);
        try
        {
            return JsonSerializer.Deserialize<List<ProjectRecord>>(json) ?? new List<ProjectRecord>();
        }
        catch (JsonException exception)
        {
            // Left to propagate as before - the point of the log is that the file on disk,
            // not the request, is what is broken, which the caller's error will not say.
            _logger.Error(exception, "The project list at {StorePath} is not valid JSON", filePath);
            throw;
        }
    }

    private void Write(ShortGuid userId, IReadOnlyList<ProjectRecord> projects)
    {
        var filePath = GetFilePath(userId);
        Directory.CreateDirectory(IoPath.GetDirectoryName(filePath)!);

        var json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        _logger.Debug("Wrote {Count} projects for {UserId} to {StorePath}", projects.Count, userId, filePath);
    }
}
