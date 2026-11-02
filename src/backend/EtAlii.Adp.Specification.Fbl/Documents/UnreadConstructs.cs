using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// The constructs of the <c>grammar</c> family that FBL 0.15 and 0.16 add and this library does not read (FBL 0.17 §15.1).
/// Each one a binding uses is reported as a warning that names it, at its pointer. One that stands in a rule governs the
/// text of that rule's entries, so the rule is read-only with the construct as the reason, and no splice touches its
/// entries; one that stands in the body governs text this library cannot point to, so the whole binding is read-only.
/// </summary>
internal static class UnreadConstructs
{
    /// <summary>The body's constructs: <c>prefixes</c>, <c>ordinals</c> and an <c>order</c> other than <c>document</c>.</summary>
    public static string? InBody(JsonElement body, string pointer, List<LoadDiagnostic> problems)
    {
        var found = new List<string>();
        foreach (var name in (string[])["prefixes", "ordinals", "order"])
        {
            if (!body.TryGetProperty(name, out var value)) continue;
            if (name == "order" && value.ValueKind == JsonValueKind.String && value.GetString() == "document") continue;
            found.Add(name);
            problems.Add(Warning(JsonPointer.Append(pointer, name), $"body.{name}", "0.15", "so this file is read-only"));
        }
        return found.Count == 0
            ? null
            : $"This file is read-only: this library does not read the body's {Names(found)} (FBL 0.15), so it cannot tell which text {(found.Count == 1 ? "that governs" : "they govern")} (FBL 0.17 §15.1).";
    }

    /// <summary>A rule's constructs, wherever they stand in it: its insert's <c>continues</c>, its remove's <c>parts</c> and
    /// <c>whole</c>, and a <c>{node}</c> slot's <c>expand</c>, <c>ordinal</c>, <c>name</c> and the escapes of 0.15 but <c>requote</c>.</summary>
    public static string? InRule(JsonElement rule, string type, string pointer, List<LoadDiagnostic> problems)
    {
        var found = new List<Construct>();
        Walk(rule, null, pointer, found);
        if (found.Count == 0) return null;
        foreach (var construct in found) problems.Add(Warning(construct.Pointer, construct.Name, construct.Version, $"so no {type} is changed in this file"));
        return $"No {type} can be changed in this file: this library does not read its {Names(found.Select(construct => construct.Name).Distinct().ToList())} (FBL 0.17 §15.1).";
    }

    private static void Walk(JsonElement json, string? parent, string pointer, List<Construct> found)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in json.EnumerateArray()) Walk(item, parent, $"{pointer}/{index++}", found);
            return;
        }
        if (json.ValueKind != JsonValueKind.Object) return;
        var slot = json.TryGetProperty("node", out _);
        foreach (var property in json.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer, property.Name);
            Construct? construct = (parent, property.Name) switch
            {
                ("insert", "continues") => new Construct("insert.continues", at, "0.16"),
                ("remove", "parts" or "whole") => new Construct($"remove.{property.Name}", at, "0.16"),
                ("escape", "sequences" or "unicode" or "writes") => new Construct($"escape.{property.Name}", at, "0.15"),
                (_, "expand" or "ordinal" or "name") when slot => new Construct(property.Name, at, "0.15"),
                _ => null,
            };
            if (construct is not null) found.Add(construct);
            else Walk(property.Value, property.Name, at, found);
        }
    }

    /// <summary>A construct a rule uses: its name as a warning gives it, where it stands, and the FBL version that adds it.</summary>
    private sealed record Construct(string Name, string Pointer, string Version);

    private static LoadDiagnostic Warning(string pointer, string construct, string version, string consequence) =>
        new(pointer, LoadSeverity.Warning, $"This library does not read '{construct}' (FBL {version}); the text it governs is kept byte for byte, {consequence} (FBL 0.17 §15.1).");

    private static string Names(List<string> names) => string.Join(" and ", names.Select(name => $"'{name}'"));
}
