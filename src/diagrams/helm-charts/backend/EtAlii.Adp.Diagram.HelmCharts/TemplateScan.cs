using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// The literal-fact line scanner for <c>templates/</c> content: what a Go-templated manifest
/// says about itself on lines that need no rendering to read.
/// </summary>
/// <remarks>
/// <para>
/// A template is deliberately never parsed as YAML - it is not YAML until rendered
/// (Requirement 3.2) - so everything known about one comes from scanning its lines for facts
/// that are literally written: a <c>kind:</c> or <c>apiVersion:</c> whose value carries no
/// template expression, a <c>{{ define "name" }}</c> in a partial, and
/// <c>{{ include "name" ... }}</c>/<c>{{ template "name" ... }}</c> references whose name is a
/// literal double-quoted string. A templated fact (<c>kind: {{ .Values.kind }}</c>, a computed
/// include name) is unknown by design, never guessed at.
/// </para>
/// <para>
/// <c>kind:</c>/<c>apiVersion:</c> count only at column zero: a manifest's own keys sit at the
/// top level of their document, while an indented <c>kind:</c> (inside an RBAC rule, a list
/// entry, a commented example) belongs to something else. This is the whole "what counts as
/// literal" decision set, kept in one class and pinned by fixtures so it stays a set of test
/// cases rather than folklore (the design's NFR).
/// </para>
/// </remarks>
public static class TemplateScan
{
    private static readonly Regex _define = new(
        """\{\{-?\s*define\s+"([^"]+)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex _reference = new(
        """\{\{-?\s*(?:include|template)\s+"([^"]+)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Scans one template's text. Pure: the caller owns all file access.</summary>
    public static TemplateFacts Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var apiVersions = new SortedSet<string>(StringComparer.Ordinal);
        var defines = new SortedSet<string>(StringComparer.Ordinal);
        var references = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (TopLevelValue(line, "kind:") is { } kind)
            {
                kinds.Add(kind);
            }

            if (TopLevelValue(line, "apiVersion:") is { } apiVersion)
            {
                apiVersions.Add(apiVersion);
            }

            foreach (Match match in _define.Matches(line))
            {
                defines.Add(match.Groups[1].Value);
            }

            foreach (Match match in _reference.Matches(line))
            {
                references.Add(match.Groups[1].Value);
            }
        }

        return new TemplateFacts([.. kinds], [.. apiVersions], [.. defines], [.. references]);
    }

    /// <summary>
    /// The literal value of a column-zero <c>key:</c> line, or null when the line is not that
    /// key, its value is templated, or nothing is left once the trailing comment and quotes go.
    /// </summary>
    private static string? TopLevelValue(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return null;
        }

        var value = line[key.Length..];

        // A YAML comment after the value is not part of it.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0)
        {
            value = value[..comment];
        }

        value = value.Trim().Trim('"', '\'').Trim();

        if (value.Length == 0 || value.Contains("{{", StringComparison.Ordinal))
        {
            // Templated or absent: unknown by design, never guessed.
            return null;
        }

        return value;
    }
}
