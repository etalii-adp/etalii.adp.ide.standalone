namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>Where the repository and the vendored conformance files are.</summary>
internal static class Repository
{
    public static string Root => RepositoryPaths.Root;

    /// <summary>The FBL examples, fixtures and registrations vendored unchanged from etalii-adp/etalii.adp.</summary>
    public static string Conformance => Path.Combine(Root, "src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Conformance");
}
