namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>Where the repository and the vendored conformance files are, found from the test assembly.</summary>
internal static class Repository
{
    private static readonly Lazy<string> _root = new(() =>
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "src", "backend", "EtAlii.Adp.slnx"))) return folder.FullName;
        }
        throw new InvalidOperationException("The repository root (the folder holding src/backend/EtAlii.Adp.slnx) was not found above the test assembly.");
    });

    public static string Root => _root.Value;

    public static string Source => Path.Combine(Root, "src");

    /// <summary>The FBL examples, fixtures and registrations vendored unchanged from etalii-adp/etalii.adp.</summary>
    public static string Conformance => Path.Combine(Root, "src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance");
}
