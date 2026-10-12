using System.Text.RegularExpressions;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>Where a schema reference points: a file of the project, a schema ADP bundles, or none.</summary>
internal abstract record SchemaTarget
{
    /// <summary>A file of the project, by its path from the root, its file name last.</summary>
    public sealed record Project(IReadOnlyList<string> Path) : SchemaTarget;

    /// <summary>A schema ADP ships, by the address a document names it with.</summary>
    public sealed record Bundled(string Id) : SchemaTarget;

    /// <summary>No schema, with the reason that is said to the user after the reference.</summary>
    public sealed record None(string Reason) : SchemaTarget;
}

/// <summary>
/// The C# twin of the client's <c>resolveSchemaReference</c> (json-editor Requirement 9.5,
/// xml-editor Requirement 9.6): one set of rules, held to <c>src/fixtures/schema-reference-cases.json</c>
/// from both sides, so the two cannot drift. A pure function with no I/O: the raw reference is
/// decided before any URI arithmetic, so a <c>..</c> that would step above the project root is
/// refused with a reason, never clamped.
/// </summary>
internal static partial class SchemaReferenceRules
{
    /// <param name="reference">The reference as the document holds it.</param>
    /// <param name="documentPath">The document's path in the project, its file name last.</param>
    /// <param name="bundled">The addresses ADP ships a schema for.</param>
    public static SchemaTarget Resolve(string reference, IReadOnlyList<string> documentPath, IReadOnlySet<string> bundled)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentNullException.ThrowIfNull(bundled);

        var trimmed = reference.Trim();
        if (trimmed.Length == 0)
        {
            return new SchemaTarget.None("is empty");
        }

        // A fragment names a part of a file, not another file; an empty one is the same address.
        var hash = trimmed.IndexOf('#', StringComparison.Ordinal);
        var address = hash == -1 ? trimmed : trimmed[..hash];

        if (bundled.Contains(address))
        {
            return new SchemaTarget.Bundled(address);
        }

        if (Scheme().IsMatch(address))
        {
            return WebScheme().IsMatch(address)
                ? new SchemaTarget.None("is a web address; ADP reads schemas from the project")
                : new SchemaTarget.None("is not a file of the project");
        }

        if (address.StartsWith('/'))
        {
            return new SchemaTarget.None("is an absolute path; ADP reads a schema relative to the document");
        }

        var segments = address.Split('/');
        if (segments[^1] is "" or "." or "..")
        {
            return new SchemaTarget.None("names a folder, not a file");
        }

        var resolved = documentPath.Take(documentPath.Count - 1).ToList();
        foreach (var segment in segments)
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolved.Count == 0)
                {
                    return new SchemaTarget.None("leaves the project");
                }

                resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            resolved.Add(segment);
        }

        return new SchemaTarget.Project(resolved);
    }

    /// <summary>A URI scheme, as RFC 3986 spells one: a letter, then letters, digits, <c>+</c>, <c>-</c> or <c>.</c>, then <c>:</c>.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex("^https?:", RegexOptions.IgnoreCase)]
    private static partial Regex WebScheme();
}
