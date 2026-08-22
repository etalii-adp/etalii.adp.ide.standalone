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

    /// <summary>Whether <see cref="Initialize"/> has run in this process - distinct from "found nothing".</summary>
    internal static bool IsInitialized { get; private set; }

    private static readonly Lock InitializeGate = new();

    /// <summary>
    /// Fills <see cref="All"/> - the first time - by running <paramref name="discover"/>. The
    /// cache is per process, so once it is filled a later call runs nothing, leaves the list
    /// exactly as it was and reports <c>false</c>. That later call is not an error: a test
    /// process hosts the application several times over (one <c>WebApplicationFactory</c>
    /// per test), and each host runs the same startup - concurrently, which is why the check
    /// and the fill happen under one lock, so the scan runs exactly once.
    /// </summary>
    /// <returns><c>true</c> when this call ran the scan and filled the cache; <c>false</c> when it was already filled.</returns>
    internal static bool Initialize(Func<IReadOnlyList<DiagramDefinition>> discover)
    {
        ArgumentNullException.ThrowIfNull(discover);

        lock (InitializeGate)
        {
            if (IsInitialized)
            {
                return false;
            }

            All = discover() ?? throw new InvalidOperationException("Discovery returned null.");
            IsInitialized = true;
            return true;
        }
    }

    /// <summary>
    /// Test-only: forgets the cache so a test can exercise <see cref="Initialize"/> from a
    /// clean state. Never called by the application.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (InitializeGate)
        {
            All = [];
            IsInitialized = false;
        }
    }
}
