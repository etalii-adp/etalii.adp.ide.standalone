namespace EtAlii.Adp.Diagram;

/// <summary>
/// What a diagram-type module is: its origin/notation and its display title - mirroring one row
/// of docs/diagrams.md's catalog table. Each diagram-type project exposes exactly one of these
/// through its own static <c>Diagram.Definition</c>.
/// </summary>
public sealed record DiagramDefinition(DiagramOrigin Origin, string Title)
{
    /// <summary>
    /// Every diagram type discovered at startup, in a stable order. Empty until the host has
    /// run <see cref="DiagramDefinitionDiscovery"/> and handed the result to
    /// <see cref="Initialize"/>; never null.
    /// </summary>
    /// <remarks>
    /// Publicly a getter only: the cache is filled exactly once and there is no public way to replace
    /// it. It is not a lazy static because a static initializer could be given neither the
    /// host's logger (the scan has to report what it found) nor a test's own assemblies (the
    /// scan has to be testable without depending on which modules happen to be built) - so
    /// the host fills it explicitly, and everything else just reads.
    /// </remarks>
    public static IReadOnlyList<DiagramDefinition> All { get; private set; } = [];

    private static bool Initialized { get; set; }

    /// <summary>
    /// Fills <see cref="All"/>. Called once by the host after discovery; a second call is a
    /// programming error and throws rather than silently replacing the list.
    /// </summary>
    internal static void Initialize(IReadOnlyList<DiagramDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        if (Initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(DiagramDefinition)}.{nameof(All)} has already been initialized; it is filled exactly once per process.");
        }

        All = definitions;
        Initialized = true;
    }

    /// <summary>
    /// Test-only: forgets the cache so a test can exercise <see cref="Initialize"/> from a
    /// clean state. Never called by the application.
    /// </summary>
    internal static void ResetForTests()
    {
        All = [];
        Initialized = false;
    }
}
