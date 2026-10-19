using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// Loads an FBL document as FBL §14.1 says, apart from validating against the JSON Schema (left to
/// etalii.adp's own CI): parse rejecting duplicate keys, check the version, map every construct,
/// resolve names, compile every regular expression and CEL expression, and check the rules. Every
/// problem is collected; a document with an error yields no document.
/// </summary>
public static class FblDocumentLoader
{
    /// <summary>The FBL major version this library reads, and the newest minor it reads in full; a document of a newer one loads with a warning.</summary>
    private static readonly LanguageVersion Fbl = new("FBL", 0, 5, "FBL §2.7");

    public static IReadOnlyList<LoadDiagnostic> Load(string path, out FblDocument? document) =>
        Load(ReadShared(path), out document);

    /// <summary>Reads at the sharing the repository's own reader uses, so a concurrent save is never refused.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static IReadOnlyList<LoadDiagnostic> Load(byte[] json, out FblDocument? document)
    {
        var problems = new List<LoadDiagnostic>();
        document = null;
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            problems.Add(new LoadDiagnostic("", LoadSeverity.Error, $"The document is not JSON: {e.Message}"));
            return problems;
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            problems.AddRange(JsonDuplicateKeys.Find(root, "FBL §2.1"));
            if (problems.Count > 0) return problems;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new LoadDiagnostic("", LoadSeverity.Error, "An FBL document is a JSON object."));
                return problems;
            }

            // A version this library does not read is reported, and the rest of the document still is (behaviour change B-08).
            var version = root.TryGetProperty("fbl", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : null;
            if (version is null)
            {
                problems.Add(new LoadDiagnostic("/fbl", LoadSeverity.Error, "The document does not say which FBL version it is written in."));
            }
            else if (Fbl.Check(version, "/fbl") is { } versionProblem)
            {
                problems.Add(versionProblem);
            }
            if (!root.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Object || !bindings.EnumerateObject().Any())
            {
                problems.Add(new LoadDiagnostic("/bindings", LoadSeverity.Error, "A document has at least one binding."));
                return problems;
            }

            var result = new Dictionary<string, FblBinding>(StringComparer.Ordinal);
            foreach (var property in bindings.EnumerateObject())
            {
                var pointer = JsonPointer.Append("/bindings", property.Name);
                if (!Names.IsName(property.Name))
                {
                    problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"'{property.Name}' is not a valid binding name."));
                }
                var binding = BindingReader.Read(property.Name, property.Value, pointer, problems);
                if (binding is not null)
                {
                    BindingChecker.Check(binding, pointer, problems);
                    result[property.Name] = binding;
                }
            }

            if (problems.Any(p => p.Severity == LoadSeverity.Error)) return problems;
            document = new FblDocument { Version = version!, Bindings = result };
            return problems;
        }
    }

    /// <summary>Resolves a binding reference, <c>doc.fbl#name</c> or <c>#name</c>, against <paramref name="referrer"/> (FBL §2.3).</summary>
    public static FblBinding ResolveReference(string reference, string referrer, out IReadOnlyList<LoadDiagnostic> problems)
    {
        var hash = reference.LastIndexOf('#');
        if (hash < 0) throw new ArgumentException($"'{reference}' is not a binding reference.", nameof(reference));
        var file = reference[..hash];
        var name = reference[(hash + 1)..];
        var path = file.Length == 0 ? referrer : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(referrer) ?? ".", file));
        problems = Load(path, out var document);
        if (document is null) throw new InvalidOperationException($"The FBL document '{path}' does not load: {string.Join("; ", problems)}");
        return document.Bindings.TryGetValue(name, out var binding)
            ? binding
            : throw new InvalidOperationException($"The FBL document '{path}' has no binding '{name}'.");
    }
}

internal static class Names
{
    public static bool IsName(string name)
    {
        if (name.Length == 0) return false;
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_')) return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')) return false;
        }
        return true;
    }
}

/// <summary>Maps a binding's JSON onto the typed records, reporting what it cannot map.</summary>
internal static class BindingReader
{
    public static FblBinding? Read(string name, JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A binding is an object."));
            return null;
        }
        if (!json.TryGetProperty("claims", out var claims)) problems.Add(new LoadDiagnostic(pointer + "/claims", LoadSeverity.Error, "A binding has claims."));
        if (!json.TryGetProperty("body", out var body)) problems.Add(new LoadDiagnostic(pointer + "/body", LoadSeverity.Error, "A binding has a body."));
        if (!json.TryGetProperty("reader", out var reader)) problems.Add(new LoadDiagnostic(pointer + "/reader", LoadSeverity.Error, "A binding has a reader."));
        if (problems.Any(p => p.Severity == LoadSeverity.Error && p.Pointer.StartsWith(pointer, StringComparison.Ordinal))) return null;

        PluginReader? plugin = null;
        if (reader.ValueKind == JsonValueKind.Object)
        {
            plugin = new PluginReader(JsonProperties.String(reader, "plugin") ?? "", JsonProperties.String(reader, "version"));
        }
        else if (reader.ValueKind != JsonValueKind.String || reader.GetString() != "declared")
        {
            problems.Add(new LoadDiagnostic(pointer + "/reader", LoadSeverity.Error, "The reader is \"declared\" or a plugin."));
        }

        return new FblBinding
        {
            Name = name,
            Title = Localized(json, "title"),
            Claims = ReadClaims(claims),
            Body = ReadBody(body, pointer + "/body", problems),
            Plugin = plugin,
            ReadOnly = ReadOnly(json),
            Text = ReadText(json),
            Header = json.TryGetProperty("header", out var h)
                ? new HeaderSettings(JsonProperties.String(h, "key"), h.TryGetProperty("value", out var hv) ? hv.Clone() : null, JsonProperties.String(h, "line"), JsonProperties.Bool(h, "required"))
                : null,
            Comment = JsonProperties.String(json, "comment"),
            ReportUnmatched = JsonProperties.String(json, "unmatched") == "report",
            Blocks = Array(json, "blocks").Select(b => new BlockRule(JsonProperties.String(b, "name") ?? "", JsonProperties.String(b, "line") ?? "", OptionalStrings(b, "within"), JsonProperties.Bool(b, "caseInsensitive"), JsonProperties.String(b, "view"))).ToList(),
            Elements = Array(json, "elements").Select(e => ReadRule(e, false)).ToList(),
            Relations = Array(json, "relations").Select(e => ReadRule(e, true)).ToList(),
            Registration = ReadRegistration(json),
            Template = json.TryGetProperty("template", out var t)
                ? new TemplateSettings(JsonProperties.String(t, "text") ?? "", t.TryGetProperty("byOrigin", out var bo) ? bo.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>())
                : null,
        };
    }

    private static Claims ReadClaims(JsonElement json)
    {
        Marker? marker = null;
        if (json.TryGetProperty("marker", out var m))
        {
            marker = new Marker(JsonProperties.String(m, "rootKey"), m.TryGetProperty("value", out var mv) ? mv.Clone() : null, JsonProperties.String(m, "firstLine"), JsonProperties.String(m, "pattern"),
                m.TryGetProperty("lines", out var l) && l.TryGetInt32(out var n) ? n : 20);
        }
        var readings = new Dictionary<string, ReadingClaim>(StringComparer.Ordinal);
        if (json.TryGetProperty("readings", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                readings[p.Name] = new ReadingClaim(JsonProperties.Bool(p.Value, "bare"), p.Value.TryGetProperty("suggest", out var s) ? JsonProperties.Strings(s, "contains") : []);
            }
        }
        return new Claims
        {
            Extensions = JsonProperties.Strings(json, "extensions"),
            Names = JsonProperties.Strings(json, "names"),
            Shared = JsonProperties.Bool(json, "shared"),
            RegistrationOnly = JsonProperties.Bool(json, "registrationOnly"),
            Marker = marker,
            Suggest = json.TryGetProperty("suggest", out var sg) ? JsonProperties.Strings(sg, "contains") : [],
            Origins = JsonProperties.Strings(json, "origins"),
            Readings = readings,
        };
    }

    private static BodySettings ReadBody(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var kind = JsonProperties.String(json, "kind");
        if (kind is not ("file" or "folder")) problems.Add(new LoadDiagnostic(pointer + "/kind", LoadSeverity.Error, "A body's kind is \"file\" or \"folder\"."));
        var recognise = json.TryGetProperty("recognise", out var r) ? r : default;
        var settle = BodySettings.DefaultSettle;
        if (json.TryGetProperty("settle", out var t) && !(t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out settle) && settle >= 0))
        {
            problems.Add(new LoadDiagnostic(pointer + "/settle", LoadSeverity.Error, "A body's settle is a whole number of milliseconds, 0 or more."));
            settle = BodySettings.DefaultSettle;
        }
        return new BodySettings
        {
            IsFolder = kind == "folder",
            Family = ParseFamily(JsonProperties.String(json, "family")),
            AlsoRead = JsonProperties.Strings(json, "alsoRead").Select(ParseFamily).OfType<Family>().ToList(),
            RecogniseAll = recognise.ValueKind == JsonValueKind.Object ? JsonProperties.Strings(recognise, "all") : [],
            RecogniseAny = recognise.ValueKind == JsonValueKind.Object ? JsonProperties.Strings(recognise, "any") : [],
            RecogniseNone = recognise.ValueKind == JsonValueKind.Object ? JsonProperties.Strings(recognise, "none") : [],
            Files = Array(json, "files").Select(f => new FileRule(JsonProperties.String(f, "name"), JsonProperties.String(f, "glob") ?? "", ParseFamily(JsonProperties.String(f, "family")))).ToList(),
            Ignore = JsonProperties.Strings(json, "ignore"),
            Settle = settle,
        };
    }

    private static Family? ParseFamily(string? name) => name switch
    {
        "yaml" => Family.Yaml,
        "json" => Family.Json,
        "xml" => Family.Xml,
        "lines" => Family.Lines,
        "blocks" => Family.Blocks,
        _ => null,
    };

    private static TextDefaults ReadText(JsonElement json)
    {
        if (!json.TryGetProperty("text", out var t)) return new TextDefaults();
        var indent = 2;
        if (t.TryGetProperty("indent", out var i))
        {
            indent = i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
        }
        return new TextDefaults
        {
            Newline = JsonProperties.String(t, "newline") switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" },
            Indent = indent,
            SequenceFlush = JsonProperties.String(t, "sequenceIndent") == "flush",
            Quote = JsonProperties.String(t, "quote") ?? "double",
        };
    }

    private static Rule ReadRule(JsonElement json, bool relation)
    {
        IdBinding? id = null;
        if (json.TryGetProperty("id", out var idJson))
        {
            id = new IdBinding(idJson.TryGetProperty("from", out var from) ? ReadSlot(from) : null,
                idJson.TryGetProperty("sidecar", out var sc) ? JsonProperties.String(sc, "key") : null);
        }
        ParentBinding? parent = null;
        if (json.TryGetProperty("parent", out var p))
        {
            parent = new ParentBinding(JsonProperties.Strings(p, "rules"), JsonProperties.String(p, "slot"));
        }
        var attributes = new List<KeyValuePair<string, AttributeBinding>>();
        if (json.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in attrs.EnumerateObject()) attributes.Add(new KeyValuePair<string, AttributeBinding>(a.Name, ReadAttribute(a.Value)));
        }
        InsertSettings? insert = null;
        if (json.TryGetProperty("insert", out var ins))
        {
            // {before: key} is recognised so every family can refuse it; no family places by it yet.
            var pl = ins.GetProperty("place");
            var place = pl.ValueKind == JsonValueKind.Object ? "before" : pl.GetString() ?? "end";
            CreateContainer? create = null;
            if (ins.TryGetProperty("create", out var c))
            {
                var at = c.GetProperty("at");
                if (at.ValueKind == JsonValueKind.String) create = new CreateContainer(at.GetString()!, null, JsonProperties.String(c, "text"));
                else
                {
                    var prop = at.EnumerateObject().First();
                    create = new CreateContainer(prop.Name, prop.Value.GetString(), JsonProperties.String(c, "text"));
                }
            }
            insert = new InsertSettings
            {
                Place = place,
                Container = JsonProperties.String(ins, "container"),
                Create = create,
                Keys = JsonProperties.Strings(ins, "keys"),
                Emit = JsonProperties.String(ins, "emit"),
                Skeleton = JsonProperties.String(ins, "skeleton"),
                When = JsonProperties.String(ins, "when"),
            };
        }
        RemoveSettings? remove = null;
        if (json.TryGetProperty("remove", out var rem))
        {
            var container = JsonProperties.String(rem, "container");
            remove = new RemoveSettings(JsonProperties.Strings(rem, "cascade"), container is "remove-when-empty" or "remove-empty-levels", container == "remove-empty-levels");
        }
        return new Rule
        {
            Name = JsonProperties.String(json, "name") ?? "",
            Type = JsonProperties.String(json, "type") ?? "",
            IsRelation = relation,
            At = JsonProperties.String(json, "at"),
            Line = JsonProperties.String(json, "line"),
            Within = OptionalStrings(json, "within"),
            Opens = JsonProperties.Bool(json, "opens"),
            CaseInsensitive = JsonProperties.Bool(json, "caseInsensitive"),
            Files = JsonProperties.Strings(json, "files"),
            When = JsonProperties.String(json, "when"),
            Id = id,
            Parent = parent,
            Attributes = attributes,
            Source = json.TryGetProperty("source", out var s) ? ReadAttribute(s) : null,
            Target = json.TryGetProperty("target", out var t) ? ReadAttribute(t) : null,
            Insert = insert,
            Remove = remove,
            SnapshotUndo = JsonProperties.String(json, "undo") == "snapshot",
            ReadOnly = ReadOnly(json),
        };
    }

    private static Slot ReadSlot(JsonElement json) => new()
    {
        Key = JsonProperties.String(json, "key"),
        XmlAttribute = JsonProperties.String(json, "attribute"),
        Text = JsonProperties.Bool(json, "text"),
        Child = JsonProperties.String(json, "child"),
        Group = JsonProperties.String(json, "group"),
        Parent = JsonProperties.String(json, "parent"),
        Capture = JsonProperties.String(json, "capture"),
        Value = JsonProperties.String(json, "value"),
        Word = JsonProperties.String(json, "word"),
        Flag = JsonProperties.Bool(json, "flag"),
    };

    private static AttributeBinding ReadAttribute(JsonElement json)
    {
        var slot = ReadSlot(json);
        int? decimals = null;
        if (json.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Object && n.TryGetProperty("decimals", out var d))
        {
            decimals = d.GetInt32();
        }
        IReadOnlyList<KeyValuePair<string, string>>? map = null;
        if (json.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            map = m.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, ScalarText(p.Value))).ToList();
        }
        CreateChild? create = null;
        if (json.TryGetProperty("create", out var c))
        {
            var pl = c.TryGetProperty("place", out var place) ? place : default;
            create = JsonProperties.String(c, "at") == "end"
                ? new CreateChild("", "last", null, true)
                : pl.ValueKind == JsonValueKind.Object
                ? new CreateChild(JsonProperties.String(c, "emit") ?? "", "before", JsonProperties.String(pl, "before"))
                : new CreateChild(JsonProperties.String(c, "emit") ?? "", pl.ValueKind == JsonValueKind.String ? pl.GetString()! : "last", null);
        }
        ReferenceBinding? reference = null;
        if (json.TryGetProperty("reference", out var r))
        {
            reference = new ReferenceBinding(JsonProperties.Strings(r, "to"), JsonProperties.String(r, "by") ?? "");
        }
        return new AttributeBinding
        {
            Key = slot.Key,
            XmlAttribute = slot.XmlAttribute,
            Text = slot.Text,
            Child = slot.Child,
            Group = slot.Group,
            Parent = slot.Parent,
            Capture = slot.Capture,
            Value = slot.Value,
            Word = slot.Word,
            Flag = slot.Flag,
            Empty = JsonProperties.String(json, "empty"),
            Absent = json.TryGetProperty("absent", out var a) && a.ValueKind == JsonValueKind.Object
                ? a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "insert")
                : new Dictionary<string, string>(),
            Default = json.TryGetProperty("default", out var def) ? def.Clone() : null,
            Decimals = decimals,
            KeepTimePrecision = JsonProperties.String(json, "time") == "keep-precision",
            Style = JsonProperties.String(json, "style"),
            Reference = reference,
            Map = map,
            Override = json.TryGetProperty("override", out var o) ? ReadSlot(o) : null,
            HtmlParagraphs = JsonProperties.String(json, "content") == "html-paragraphs",
            Create = create,
            ReadOnly = ReadOnly(json),
            Read = JsonProperties.String(json, "read"),
            Quoted = JsonProperties.String(json, "quoted"),
            Fallback = json.TryGetProperty("fallback", out var f) && f.ValueKind == JsonValueKind.Object
                ? new FallbackBinding(f.TryGetProperty("value", out var fv) ? fv.Clone() : default, !f.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.False)
                : null,
        };
    }

    private static RegistrationSettings ReadRegistration(JsonElement json)
    {
        if (!json.TryGetProperty("registration", out var r)) return new RegistrationSettings();
        return new RegistrationSettings
        {
            Headers = r.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object ? h.EnumerateObject().Select(p => p.Name).ToList() : [],
            CreateOnFirstPlacement = JsonProperties.Bool(r, "createOnFirstPlacement"),
            ResourceCapture = r.TryGetProperty("resource", out var res) ? JsonProperties.String(res, "capture") : null,
            LegacyLayout = JsonProperties.String(r, "legacyLayout"),
        };
    }

    internal static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    private static string? ReadOnly(JsonElement json)
    {
        if (!json.TryGetProperty("readOnly", out var r)) return null;
        return r.ValueKind switch
        {
            JsonValueKind.True => "",
            JsonValueKind.False => null,
            JsonValueKind.String => r.GetString(),
            JsonValueKind.Object => LocalizedValue(r),
            _ => null,
        };
    }

    private static string? Localized(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : LocalizedValue(v) : null;

    private static string? LocalizedValue(JsonElement value) =>
        value.TryGetProperty("en", out var en) ? en.GetString() : value.EnumerateObject().Select(p => p.Value.GetString()).FirstOrDefault();

    private static IReadOnlyList<string>? OptionalStrings(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _) ? JsonProperties.Strings(json, name) : null;

    private static IEnumerable<JsonElement> Array(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];
}

/// <summary>The checks of FBL §14.1 steps 4 to 6 that need no DISL specification.</summary>
internal static class BindingChecker
{
    public static void Check(FblBinding binding, string pointer, List<LoadDiagnostic> problems)
    {
        var rules = binding.AllRules.ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ruleNames = rules.Select(r => r.Name).Concat(binding.Blocks.Select(b => b.Name)).ToHashSet(StringComparer.Ordinal);
        var fileRules = binding.Body.Files.Select(f => f.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < binding.Blocks.Count; i++)
        {
            var block = binding.Blocks[i];
            var at = $"{pointer}/blocks/{i}";
            if (!names.Add(block.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"The rule name '{block.Name}' is used twice."));
            CheckRegex(block.Line, at + "/line", problems);
            CheckWithin(block.Within, ruleNames, at + "/within", problems);
        }

        var elementCount = binding.Elements.Count;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var at = rule.IsRelation ? $"{pointer}/relations/{i - elementCount}" : $"{pointer}/elements/{i}";
            if (!Names.IsName(rule.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"'{rule.Name}' is not a valid rule name."));
            if (!names.Add(rule.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"The rule name '{rule.Name}' is used twice."));
            if ((rule.At is null) == (rule.Line is null) && binding.Plugin is null)
            {
                problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A rule has exactly one of 'at' and 'line'."));
            }
            if (rule.Line is not null) CheckRegex(rule.Line, at + "/line", problems);
            CheckWithin(rule.Within, ruleNames, at + "/within", problems);
            foreach (var file in rule.Files)
            {
                if (!fileRules.Contains(file)) problems.Add(new LoadDiagnostic(at + "/files", LoadSeverity.Error, $"No file rule is named '{file}'."));
            }
            if (rule.Parent is not null)
            {
                foreach (var p in rule.Parent.Rules) Require(p, rules, at + "/parent/rules", problems);
            }
            if (rule.Remove is not null)
            {
                foreach (var c in rule.Remove.Cascade) Require(c, rules, at + "/remove/cascade", problems);
            }
            var families = FamiliesOf(binding, rule);
            var scalars = families.Count > 0 && families.All(f => f is Family.Yaml or Family.Json);
            var context = rule.At is null ? CelContext.Lines : scalars ? CelContext.Scalars : CelContext.Tree;
            Compile(rule.When, context, at + "/when", problems);
            Compile(rule.Insert?.When, CelContext.Insert, at + "/insert/when", problems);
            if (rule.Id?.From is { } from) CheckSlot(from, context, at + "/id/from", problems);
            if (rule.Id?.SidecarKey is { } sidecar) Compile(sidecar, context, at + "/id/sidecar/key", problems);
            if (rule.IsRelation && (rule.Source is null || rule.Target is null))
            {
                problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A relation rule has a source and a target."));
            }
            foreach ((string name, AttributeBinding attribute) in rule.Attributes)
            {
                var a = $"{at}/attributes/{JsonPointer.Token(name)}";
                CheckSlot(attribute, context, a, problems);
                if (attribute.Reference is { } reference)
                {
                    foreach (var to in reference.To) Require(to, rules, a + "/reference/to", problems);
                }
                if (attribute.Override is { } o) CheckSlot(o, context, a + "/override", problems);
                CheckScalarOptions(attribute, scalars, a, problems);
                if (CountSlots(attribute) != 1)
                {
                    problems.Add(new LoadDiagnostic(a, LoadSeverity.Error, "An attribute binding names exactly one slot (FBL §3.3)."));
                }
            }
            if (rule.Source is not null) CheckSlot(rule.Source, context, at + "/source", problems);
            if (rule.Target is not null) CheckSlot(rule.Target, context, at + "/target", problems);
        }

        var family = binding.Body.Family;
        if (binding.Plugin is null)
        {
            if (!binding.Body.IsFolder && family is null)
            {
                problems.Add(new LoadDiagnostic(pointer + "/body/family", LoadSeverity.Error, "A declared file body names its family."));
            }
            if (rules.Count == 0)
            {
                problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A declared binding has at least one element or relation rule."));
            }
        }
        else if (rules.Count > 0)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A binding read by a plugin has no rules; its model is what the plugin reads."));
        }
        if (binding.Claims is { Shared: true, Marker: null, RegistrationOnly: false })
        {
            problems.Add(new LoadDiagnostic(pointer + "/claims", LoadSeverity.Error, "A shared claim has a marker or is registrationOnly."));
        }
        if (binding.Claims.Readings.Values.Count(r => r.Bare) > 1)
        {
            problems.Add(new LoadDiagnostic(pointer + "/claims/readings", LoadSeverity.Error, "At most one reading is bare."));
        }
        if (binding.Comment is not null) CheckRegex(binding.Comment, pointer + "/comment", problems);
        if (binding.Header?.Line is { } headerLine) CheckRegex(headerLine, pointer + "/header/line", problems);
        if (binding.Claims.Marker?.Pattern is { } pattern) CheckRegex(pattern, pointer + "/claims/marker/pattern", problems);
    }

    /// <summary>The families a rule reads: those of the file rules it names, else the body's, else those of every file rule.</summary>
    private static List<Family> FamiliesOf(FblBinding binding, Rule rule)
    {
        if (rule.Files.Count > 0) return binding.Body.Files.Where(f => f.Name is { } name && rule.Files.Contains(name)).Select(f => f.Family).OfType<Family>().ToList();
        if (binding.Body.Family is { } family) return [family];
        return binding.Body.Files.Select(f => f.Family).OfType<Family>().ToList();
    }

    /// <summary>
    /// FBL 0.5 §5.2: <c>read</c>, <c>quoted</c> and <c>fallback</c> are given only on a slot of the yaml
    /// or json family that reads a scalar, so never in another family nor on a <c>value</c> or <c>capture</c> slot.
    /// </summary>
    private static void CheckScalarOptions(AttributeBinding attribute, bool scalars, string pointer, List<LoadDiagnostic> problems)
    {
        var where = !scalars ? "a slot of the xml, lines or blocks family" : attribute.Value is not null ? "a value slot" : attribute.Capture is not null ? "a capture slot" : null;
        if (where is null) return;
        foreach ((string option, bool given) in new[] { ("read", attribute.Read is not null), ("quoted", attribute.Quoted is not null), ("fallback", attribute.Fallback is not null) })
        {
            if (given) problems.Add(new LoadDiagnostic($"{pointer}/{option}", LoadSeverity.Error, $"'{option}' is not given on {where} (FBL 5.2)."));
        }
    }

    private static int CountSlots(Slot s) =>
        (s.Key is null ? 0 : 1) + (s.XmlAttribute is null ? 0 : 1) + (s.Text ? 1 : 0) + (s.Group is null ? 0 : 1) +
        (s.Parent is null ? 0 : 1) + (s.Capture is null ? 0 : 1) + (s.Value is null ? 0 : 1);

    private static void CheckSlot(Slot slot, CelContext context, string pointer, List<LoadDiagnostic> problems)
    {
        if (slot.Value is not null) Compile(slot.Value, context, pointer + "/value", problems);
        if (slot.Word is not null) CheckRegex(slot.Word, pointer + "/word", problems);
    }

    private static void Require(string name, List<Rule> rules, string pointer, List<LoadDiagnostic> problems)
    {
        if (rules.All(r => r.Name != name)) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"No rule is named '{name}'."));
    }

    private static void CheckWithin(IReadOnlyList<string>? within, HashSet<string> names, string pointer, List<LoadDiagnostic> problems)
    {
        if (within is null) return;
        foreach (var w in within)
        {
            if (w != "^" && !names.Contains(w)) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"No rule is named '{w}'."));
        }
    }

    private static void CheckRegex(string expression, string pointer, List<LoadDiagnostic> problems)
    {
        var problem = RegexSubset.Check(expression);
        if (problem is not null) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, problem));
    }

    private static void Compile(string? expression, CelContext context, string pointer, List<LoadDiagnostic> problems)
    {
        if (expression is null) return;
        try
        {
            FblCel.Compile(expression, context);
        }
        catch (CelException e)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, e.Message));
        }
    }
}
