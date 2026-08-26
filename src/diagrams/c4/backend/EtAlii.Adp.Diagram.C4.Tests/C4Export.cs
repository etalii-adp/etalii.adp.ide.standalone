using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The Mermaid diagrams Structurizr drew from a fixture, read from <c>Fixtures/exports</c>.
/// </summary>
/// <remarks>
/// The same trade the verdicts make, for the same reason: the CLI produces these, they are
/// committed, and the everyday run reads the file. What the CLI-gated
/// <c>C4InteropTests.EveryExport_IsStillWhatStructurizrDraws</c> adds is proof that the file has
/// not drifted from what the tool draws today.
/// </remarks>
public static class C4Export
{
    /// <summary>How to regenerate the exports, named in every failure so nobody has to go looking.</summary>
    public const string RegenerationHint =
        "Regenerate the exports with the Structurizr CLI " +
        "(`structurizr export -workspace <fixture> -format mermaid -output Fixtures/exports/<fixture>`) " +
        "and see C4InteropTests.EveryExport_IsStillWhatStructurizrDraws.";

    private static string DirectoryPath => IoPath.Combine("Fixtures", "exports");

    /// <summary>Every committed diagram for <paramref name="fixture"/>, by file name.</summary>
    /// <remarks>
    /// Throws on a fixture with no exports at all. A fixture that draws nothing is either a
    /// defect or an omission, and both deserve to be said out loud rather than passed over.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Read(string fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var directory = IoPath.Combine(DirectoryPath, IoPath.GetFileNameWithoutExtension(fixture));
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"No committed exports for '{fixture}'. Every fixture in the corpus needs them. {RegenerationHint}");
        }

        var diagrams = Directory.GetFiles(directory, "*.mmd")
            .ToDictionary(IoPath.GetFileName, File.ReadAllText, StringComparer.Ordinal);

        if (diagrams.Count == 0)
        {
            throw new FileNotFoundException(
                $"'{directory}' holds no diagrams, so nothing was drawn for '{fixture}'. {RegenerationHint}");
        }

        return diagrams;
    }

    /// <summary>
    /// Whether <paramref name="diagram"/> draws something labelled <paramref name="element"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mermaid draws an element two ways depending on whether anything sits inside it. A leaf
    /// is a node whose label carries the name in a bold HTML fragment; anything with children -
    /// every deployment and infrastructure node in the corpus - is a <c>subgraph</c> titled with
    /// the name. Looking for only the first form found four fixtures' worth of nodes
    /// "undrawn" when Structurizr had drawn every one of them as a box.
    /// </para>
    /// <para>
    /// Both forms are matched precisely rather than by searching the text, because a name that
    /// appears in a relationship description - "Reads from the Database" - must not be enough
    /// to call the Database drawn. That is exactly how an empty view would sneak past.
    /// </para>
    /// </remarks>
    public static bool Draws(string diagram, string element)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(element);

        if (diagram.Contains($"font-weight: bold'>{element}</div>", StringComparison.Ordinal))
        {
            return true;
        }

        var title = $"[\"{element}\"]";
        return diagram
            .Split('\n')
            .Select(line => line.Trim())
            .Any(line => line.StartsWith("subgraph ", StringComparison.Ordinal) && line.EndsWith(title, StringComparison.Ordinal));
    }
}
