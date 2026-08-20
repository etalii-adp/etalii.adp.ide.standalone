using System.Text.Json;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Projects;

/// <summary>
/// Per-user project list persisted as a plain JSON file at
/// {appDataRoot}/EtAlii.Adp/users/{userId}/projects.json, per tech.md's
/// file-based (no database) storage philosophy.
/// </summary>
public sealed class FileProjectStore : IProjectStore
{
    private readonly string _appDataRoot;

    public FileProjectStore(string appDataRoot)
    {
        _appDataRoot = appDataRoot;
    }

    public IReadOnlyList<ProjectRecord> List(string userId) => Read(userId);

    public ProjectRecord Add(string userId, IReadOnlyList<string> pathSegments)
    {
        var folderPath = IoPath.Combine(pathSegments.ToArray());
        if (!Directory.Exists(folderPath))
        {
            throw new InvalidProjectPathException($"Folder not found: {folderPath}");
        }

        var projects = Read(userId).ToList();
        var name = new DirectoryInfo(folderPath).Name;
        var record = new ProjectRecord(Guid.NewGuid().ToString("N"), name, pathSegments);
        projects.Add(record);
        Write(userId, projects);
        return record;
    }

    public void Remove(string userId, string projectId)
    {
        var projects = Read(userId).Where(p => p.Id != projectId).ToList();
        Write(userId, projects);
    }

    private string GetFilePath(string userId) =>
        IoPath.Combine(_appDataRoot, "EtAlii.Adp", "users", userId, "projects.json");

    private IReadOnlyList<ProjectRecord> Read(string userId)
    {
        var filePath = GetFilePath(userId);
        if (!File.Exists(filePath))
        {
            return Array.Empty<ProjectRecord>();
        }

        var json = File.ReadAllText(filePath);
        var entries = JsonSerializer.Deserialize<List<ProjectEntry>>(json) ?? new List<ProjectEntry>();
        return entries
            .Select(e => new ProjectRecord(e.Id, e.Name, e.Path))
            .ToList();
    }

    private void Write(string userId, IReadOnlyList<ProjectRecord> projects)
    {
        var filePath = GetFilePath(userId);
        Directory.CreateDirectory(IoPath.GetDirectoryName(filePath)!);

        var entries = projects
            .Select(p => new ProjectEntry(p.Id, p.Name, p.PathSegments.ToList()))
            .ToList();
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    private sealed record ProjectEntry(string Id, string Name, List<string> Path);
}
