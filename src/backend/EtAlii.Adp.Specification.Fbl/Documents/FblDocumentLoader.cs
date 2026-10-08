using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;

namespace EtAlii.Adp.Specification.Fbl.Documents;

public enum ProblemSeverity
{
    Warning,
    Error,
}

/// <summary>A problem in an FBL document, at the JSON Pointer of its location (FBL §14.1).</summary>
public sealed record LoadProblem(string Pointer, ProblemSeverity Severity, string Message)
{
    public override string ToString() => $"{Severity} at {(Pointer.Length == 0 ? "/" : Pointer)}: {Message}";
}

/// <summary>
/// Loads an FBL document as FBL §14.1 says, apart from validating against the JSON Schema (left to
/// etalii.adp's own CI): parse rejecting duplicate keys, check the version, map every construct,
/// resolve names, compile every regular expression and CEL expression, and check the rules. Every
/// problem is collected; a document with an error yields no document.
/// </summary>
public static class FblDocumentLoader
{
    private const int SupportedMajor = 0;

    public static IReadOnlyList<LoadProblem> Load(string path, out FblDocument? document) =>
        Load(ReadShared(path), out document);

    /// <summary>Reads at the sharing the repository's own reader uses, so a concurrent save is never refused.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static IReadOnlyList<LoadProblem> Load(byte[] json, out FblDocument? document)
    {
        var problems = new List<LoadProblem>();
        document = null;
        FindDuplicateKeys(json, problems);
        if (problems.Count > 0) return problems;

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            problems.Add(new LoadProblem("", ProblemSeverity.Error, $"The document is not JSON: {e.Message}"));
            return problems;
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new LoadProblem("", ProblemSeverity.Error, "An FBL document is a JSON object."));
                return problems;
            }
            var version = root.TryGetProperty("fbl", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : null;
            if (version is null)
            {
                problems.Add(new LoadProblem("/fbl", ProblemSeverity.Error, "The document does not say which FBL version it is written in."));
                return problems;
            }
            var parts = version.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            {
                problems.Add(new LoadProblem("/fbl", ProblemSeverity.Error, $"'{version}' is not an FBL version."));
                return problems;
            }
            if (major != SupportedMajor)
            {
                problems.Add(new LoadProblem("/fbl", ProblemSeverity.Error, $"FBL {version} is not supported; this library reads FBL {SupportedMajor}.x."));
                return problems;
            }
            if (minor > 1)
            {
                problems.Add(new LoadProblem("/fbl", ProblemSeverity.Warning, $"FBL {version} is newer than 0.1; what this library does not know is ignored."));
            }
            if (!root.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Object || !bindings.EnumerateObject().Any())
            {
                problems.Add(new LoadProblem("/bindings", ProblemSeverity.Error, "A document has at least one binding."));
                return problems;
            }

            var result = new Dictionary<string, FblBinding>(StringComparer.Ordinal);
            foreach (var property in bindings.EnumerateObject())
            {
                var pointer = "/bindings/" + Escape(property.Name);
                if (!Names.IsName(property.Name))
                {
                    problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, $"'{property.Name}' is not a valid binding name."));
                }
                var binding = BindingReader.Read(property.Name, property.Value, pointer, problems);
                if (binding is not null)
                {
                    BindingChecker.Check(binding, pointer, problems);
                    result[property.Name] = binding;
                }
            }

            if (problems.Any(p => p.Severity == ProblemSeverity.Error)) return problems;
            document = new FblDocument { Version = version, Bindings = result };
            return problems;
        }
    }

    /// <summary>Resolves a binding reference, <c>doc.fbl#name</c> or <c>#name</c>, against <paramref name="referrer"/> (FBL §2.3).</summary>
    public static FblBinding ResolveReference(string reference, string referrer, out IReadOnlyList<LoadProblem> problems)
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

    internal static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");

    private static void FindDuplicateKeys(byte[] json, List<LoadProblem> problems)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        var stack = new Stack<(HashSet<string>? Keys, string Pointer, int Index)>();
        string? pending = null;
        var pointer = "";
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        var here = Child(stack, pointer, pending);
                        if (stack.Count > 0 && stack.Peek().Keys is null)
                        {
                            var top = stack.Pop();
                            stack.Push((null, top.Pointer, top.Index + 1));
                        }
                        stack.Push((reader.TokenType == JsonTokenType.StartObject ? new HashSet<string>(StringComparer.Ordinal) : null, here, 0));
                        pending = null;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        stack.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        var keys = stack.Peek().Keys!;
                        if (!keys.Add(name))
                        {
                            problems.Add(new LoadProblem(stack.Peek().Pointer + "/" + Escape(name), ProblemSeverity.Error, $"The key '{name}' appears twice."));
                        }
                        pending = name;
                        break;
                    default:
                        if (stack.Count > 0 && stack.Peek().Keys is null)
                        {
                            var top = stack.Pop();
                            stack.Push((null, top.Pointer, top.Index + 1));
                        }
                        pending = null;
                        break;
                }
            }
        }
        catch (JsonException e)
        {
            problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, $"The document is not JSON: {e.Message}"));
        }
    }

    private static string Child(Stack<(HashSet<string>? Keys, string Pointer, int Index)> stack, string root, string? pending)
    {
        if (stack.Count == 0) return root;
        var top = stack.Peek();
        return top.Keys is null ? $"{top.Pointer}/{top.Index}" : $"{top.Pointer}/{Escape(pending ?? "")}";
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
    public static FblBinding? Read(string name, JsonElement json, string pointer, List<LoadProblem> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, "A binding is an object."));
            return null;
        }
        if (!json.TryGetProperty("claims", out var claims)) problems.Add(new LoadProblem(pointer + "/claims", ProblemSeverity.Error, "A binding has claims."));
        if (!json.TryGetProperty("body", out var body)) problems.Add(new LoadProblem(pointer + "/body", ProblemSeverity.Error, "A binding has a body."));
        if (!json.TryGetProperty("reader", out var reader)) problems.Add(new LoadProblem(pointer + "/reader", ProblemSeverity.Error, "A binding has a reader."));
        if (problems.Any(p => p.Severity == ProblemSeverity.Error && p.Pointer.StartsWith(pointer, StringComparison.Ordinal))) return null;

        PluginReader? plugin = null;
        if (reader.ValueKind == JsonValueKind.Object)
        {
            plugin = new PluginReader(Str(reader, "plugin") ?? "", Str(reader, "version"));
        }
        else if (reader.ValueKind != JsonValueKind.String || reader.GetString() != "declared")
        {
            problems.Add(new LoadProblem(pointer + "/reader", ProblemSeverity.Error, "The reader is \"declared\" or a plugin."));
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
                ? new HeaderSettings(Str(h, "key"), h.TryGetProperty("value", out var hv) ? hv.Clone() : null, Str(h, "line"), Bool(h, "required"))
                : null,
            Comment = Str(json, "comment"),
            ReportUnmatched = Str(json, "unmatched") == "report",
            Blocks = Array(json, "blocks").Select(b => new BlockRule(Str(b, "name") ?? "", Str(b, "line") ?? "", OptionalStrings(b, "within"), Bool(b, "caseInsensitive"), Str(b, "view"))).ToList(),
            Elements = Array(json, "elements").Select(e => ReadRule(e, false)).ToList(),
            Relations = Array(json, "relations").Select(e => ReadRule(e, true)).ToList(),
            Registration = ReadRegistration(json),
            Template = json.TryGetProperty("template", out var t)
                ? new TemplateSettings(Str(t, "text") ?? "", t.TryGetProperty("byOrigin", out var bo) ? bo.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>())
                : null,
        };
    }

    private static Claims ReadClaims(JsonElement json)
    {
        Marker? marker = null;
        if (json.TryGetProperty("marker", out var m))
        {
            marker = new Marker(Str(m, "rootKey"), m.TryGetProperty("value", out var mv) ? mv.Clone() : null, Str(m, "firstLine"), Str(m, "pattern"),
                m.TryGetProperty("lines", out var l) && l.TryGetInt32(out var n) ? n : 20);
        }
        var readings = new Dictionary<string, ReadingClaim>(StringComparer.Ordinal);
        if (json.TryGetProperty("readings", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in r.EnumerateObject())
            {
                readings[p.Name] = new ReadingClaim(Bool(p.Value, "bare"), p.Value.TryGetProperty("suggest", out var s) ? Strings(s, "contains") : []);
            }
        }
        return new Claims
        {
            Extensions = Strings(json, "extensions"),
            Names = Strings(json, "names"),
            Shared = Bool(json, "shared"),
            RegistrationOnly = Bool(json, "registrationOnly"),
            Marker = marker,
            Suggest = json.TryGetProperty("suggest", out var sg) ? Strings(sg, "contains") : [],
            Origins = Strings(json, "origins"),
            Readings = readings,
        };
    }

    private static BodySettings ReadBody(JsonElement json, string pointer, List<LoadProblem> problems)
    {
        var kind = Str(json, "kind");
        if (kind is not ("file" or "folder")) problems.Add(new LoadProblem(pointer + "/kind", ProblemSeverity.Error, "A body's kind is \"file\" or \"folder\"."));
        var recognise = json.TryGetProperty("recognise", out var r) ? r : default;
        return new BodySettings
        {
            IsFolder = kind == "folder",
            Family = ParseFamily(Str(json, "family")),
            AlsoRead = Strings(json, "alsoRead").Select(ParseFamily).OfType<Family>().ToList(),
            RecogniseAll = recognise.ValueKind == JsonValueKind.Object ? Strings(recognise, "all") : [],
            RecogniseAny = recognise.ValueKind == JsonValueKind.Object ? Strings(recognise, "any") : [],
            RecogniseNone = recognise.ValueKind == JsonValueKind.Object ? Strings(recognise, "none") : [],
            Files = Array(json, "files").Select(f => new FileRule(Str(f, "name"), Str(f, "glob") ?? "", ParseFamily(Str(f, "family")))).ToList(),
            Ignore = Strings(json, "ignore"),
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
            Newline = Str(t, "newline") switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" },
            Indent = indent,
            SequenceFlush = Str(t, "sequenceIndent") == "flush",
            Quote = Str(t, "quote") ?? "double",
        };
    }

    private static Rule ReadRule(JsonElement json, bool relation)
    {
        IdBinding? id = null;
        if (json.TryGetProperty("id", out var idJson))
        {
            id = new IdBinding(idJson.TryGetProperty("from", out var from) ? ReadSlot(from) : null,
                idJson.TryGetProperty("sidecar", out var sc) ? Str(sc, "key") : null);
        }
        ParentBinding? parent = null;
        if (json.TryGetProperty("parent", out var p))
        {
            parent = new ParentBinding(Strings(p, "rules"), Str(p, "slot"));
        }
        var attributes = new List<KeyValuePair<string, AttributeBinding>>();
        if (json.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in attrs.EnumerateObject()) attributes.Add(new(a.Name, ReadAttribute(a.Value)));
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
                if (at.ValueKind == JsonValueKind.String) create = new CreateContainer(at.GetString()!, null, Str(c, "text"));
                else
                {
                    var prop = at.EnumerateObject().First();
                    create = new CreateContainer(prop.Name, prop.Value.GetString(), Str(c, "text"));
                }
            }
            insert = new InsertSettings
            {
                Place = place,
                Container = Str(ins, "container"),
                Create = create,
                Keys = Strings(ins, "keys"),
                Emit = Str(ins, "emit"),
                Skeleton = Str(ins, "skeleton"),
                When = Str(ins, "when"),
            };
        }
        RemoveSettings? remove = null;
        if (json.TryGetProperty("remove", out var rem))
        {
            remove = new RemoveSettings(Strings(rem, "cascade"), Str(rem, "container") == "remove-when-empty");
        }
        return new Rule
        {
            Name = Str(json, "name") ?? "",
            Type = Str(json, "type") ?? "",
            IsRelation = relation,
            At = Str(json, "at"),
            Line = Str(json, "line"),
            Within = OptionalStrings(json, "within"),
            Opens = Bool(json, "opens"),
            CaseInsensitive = Bool(json, "caseInsensitive"),
            Files = Strings(json, "files"),
            When = Str(json, "when"),
            Id = id,
            Parent = parent,
            Attributes = attributes,
            Source = json.TryGetProperty("source", out var s) ? ReadAttribute(s) : null,
            Target = json.TryGetProperty("target", out var t) ? ReadAttribute(t) : null,
            Insert = insert,
            Remove = remove,
            SnapshotUndo = Str(json, "undo") == "snapshot",
            ReadOnly = ReadOnly(json),
        };
    }

    private static Slot ReadSlot(JsonElement json) => new()
    {
        Key = Str(json, "key"),
        XmlAttribute = Str(json, "attribute"),
        Text = Bool(json, "text"),
        Child = Str(json, "child"),
        Group = Str(json, "group"),
        Parent = Str(json, "parent"),
        Capture = Str(json, "capture"),
        Value = Str(json, "value"),
        Word = Str(json, "word"),
        Flag = Bool(json, "flag"),
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
            create = pl.ValueKind == JsonValueKind.Object
                ? new CreateChild(Str(c, "emit") ?? "", "before", Str(pl, "before"))
                : new CreateChild(Str(c, "emit") ?? "", pl.ValueKind == JsonValueKind.String ? pl.GetString()! : "last", null);
        }
        ReferenceBinding? reference = null;
        if (json.TryGetProperty("reference", out var r))
        {
            reference = new ReferenceBinding(Strings(r, "to"), Str(r, "by") ?? "");
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
            Empty = Str(json, "empty"),
            Absent = json.TryGetProperty("absent", out var a) && a.ValueKind == JsonValueKind.Object
                ? a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "insert")
                : new Dictionary<string, string>(),
            Default = json.TryGetProperty("default", out var def) ? def.Clone() : null,
            Decimals = decimals,
            KeepTimePrecision = Str(json, "time") == "keep-precision",
            Style = Str(json, "style"),
            Reference = reference,
            Map = map,
            Override = json.TryGetProperty("override", out var o) ? ReadSlot(o) : null,
            HtmlParagraphs = Str(json, "content") == "html-paragraphs",
            Create = create,
            ReadOnly = ReadOnly(json),
        };
    }

    private static RegistrationSettings ReadRegistration(JsonElement json)
    {
        if (!json.TryGetProperty("registration", out var r)) return new RegistrationSettings();
        return new RegistrationSettings
        {
            Headers = r.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object ? h.EnumerateObject().Select(p => p.Name).ToList() : [],
            CreateOnFirstPlacement = Bool(r, "createOnFirstPlacement"),
            ResourceCapture = r.TryGetProperty("resource", out var res) ? Str(res, "capture") : null,
            LegacyLayout = Str(r, "legacyLayout"),
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

    private static string? Str(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> Strings(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];

    private static IReadOnlyList<string>? OptionalStrings(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _) ? Strings(json, name) : null;

    private static IEnumerable<JsonElement> Array(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];
}

/// <summary>The checks of FBL §14.1 steps 4 to 6 that need no DISL specification.</summary>
internal static class BindingChecker
{
    public static void Check(FblBinding binding, string pointer, List<LoadProblem> problems)
    {
        var rules = binding.AllRules.ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ruleNames = rules.Select(r => r.Name).Concat(binding.Blocks.Select(b => b.Name)).ToHashSet(StringComparer.Ordinal);
        var fileRules = binding.Body.Files.Select(f => f.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < binding.Blocks.Count; i++)
        {
            var block = binding.Blocks[i];
            var at = $"{pointer}/blocks/{i}";
            if (!names.Add(block.Name)) problems.Add(new LoadProblem(at + "/name", ProblemSeverity.Error, $"The rule name '{block.Name}' is used twice."));
            CheckRegex(block.Line, at + "/line", problems);
            CheckWithin(block.Within, ruleNames, at + "/within", problems);
        }

        var elementCount = binding.Elements.Count;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var at = rule.IsRelation ? $"{pointer}/relations/{i - elementCount}" : $"{pointer}/elements/{i}";
            if (!Names.IsName(rule.Name)) problems.Add(new LoadProblem(at + "/name", ProblemSeverity.Error, $"'{rule.Name}' is not a valid rule name."));
            if (!names.Add(rule.Name)) problems.Add(new LoadProblem(at + "/name", ProblemSeverity.Error, $"The rule name '{rule.Name}' is used twice."));
            if ((rule.At is null) == (rule.Line is null) && binding.Plugin is null)
            {
                problems.Add(new LoadProblem(at, ProblemSeverity.Error, "A rule has exactly one of 'at' and 'line'."));
            }
            if (rule.Line is not null) CheckRegex(rule.Line, at + "/line", problems);
            CheckWithin(rule.Within, ruleNames, at + "/within", problems);
            foreach (var file in rule.Files)
            {
                if (!fileRules.Contains(file)) problems.Add(new LoadProblem(at + "/files", ProblemSeverity.Error, $"No file rule is named '{file}'."));
            }
            if (rule.Parent is not null)
            {
                foreach (var p in rule.Parent.Rules) Require(p, rules, at + "/parent/rules", problems);
            }
            if (rule.Remove is not null)
            {
                foreach (var c in rule.Remove.Cascade) Require(c, rules, at + "/remove/cascade", problems);
            }
            var context = rule.At is not null ? CelContext.Tree : CelContext.Lines;
            Compile(rule.When, context, at + "/when", problems);
            Compile(rule.Insert?.When, CelContext.Insert, at + "/insert/when", problems);
            if (rule.Id?.From is { } from) CheckSlot(from, context, at + "/id/from", problems);
            if (rule.Id?.SidecarKey is { } sidecar) Compile(sidecar, context, at + "/id/sidecar/key", problems);
            if (rule.IsRelation && (rule.Source is null || rule.Target is null))
            {
                problems.Add(new LoadProblem(at, ProblemSeverity.Error, "A relation rule has a source and a target."));
            }
            foreach ((string name, AttributeBinding attribute) in rule.Attributes)
            {
                var a = $"{at}/attributes/{FblDocumentLoader.Escape(name)}";
                CheckSlot(attribute, context, a, problems);
                if (attribute.Reference is { } reference)
                {
                    foreach (var to in reference.To) Require(to, rules, a + "/reference/to", problems);
                }
                if (attribute.Override is { } o) CheckSlot(o, context, a + "/override", problems);
                if (CountSlots(attribute) != 1)
                {
                    problems.Add(new LoadProblem(a, ProblemSeverity.Error, "An attribute binding names exactly one slot (FBL §3.3)."));
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
                problems.Add(new LoadProblem(pointer + "/body/family", ProblemSeverity.Error, "A declared file body names its family."));
            }
            if (rules.Count == 0)
            {
                problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, "A declared binding has at least one element or relation rule."));
            }
        }
        else if (rules.Count > 0)
        {
            problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, "A binding read by a plugin has no rules; its model is what the plugin reads."));
        }
        if (binding.Claims is { Shared: true, Marker: null, RegistrationOnly: false })
        {
            problems.Add(new LoadProblem(pointer + "/claims", ProblemSeverity.Error, "A shared claim has a marker or is registrationOnly."));
        }
        if (binding.Claims.Readings.Values.Count(r => r.Bare) > 1)
        {
            problems.Add(new LoadProblem(pointer + "/claims/readings", ProblemSeverity.Error, "At most one reading is bare."));
        }
        if (binding.Comment is not null) CheckRegex(binding.Comment, pointer + "/comment", problems);
        if (binding.Header?.Line is { } headerLine) CheckRegex(headerLine, pointer + "/header/line", problems);
        if (binding.Claims.Marker?.Pattern is { } pattern) CheckRegex(pattern, pointer + "/claims/marker/pattern", problems);
    }

    private static int CountSlots(Slot s) =>
        (s.Key is null ? 0 : 1) + (s.XmlAttribute is null ? 0 : 1) + (s.Text ? 1 : 0) + (s.Group is null ? 0 : 1) +
        (s.Parent is null ? 0 : 1) + (s.Capture is null ? 0 : 1) + (s.Value is null ? 0 : 1);

    private static void CheckSlot(Slot slot, CelContext context, string pointer, List<LoadProblem> problems)
    {
        if (slot.Value is not null) Compile(slot.Value, context, pointer + "/value", problems);
        if (slot.Word is not null) CheckRegex(slot.Word, pointer + "/word", problems);
    }

    private static void Require(string name, List<Rule> rules, string pointer, List<LoadProblem> problems)
    {
        if (rules.All(r => r.Name != name)) problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, $"No rule is named '{name}'."));
    }

    private static void CheckWithin(IReadOnlyList<string>? within, HashSet<string> names, string pointer, List<LoadProblem> problems)
    {
        if (within is null) return;
        foreach (var w in within)
        {
            if (w != "^" && !names.Contains(w)) problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, $"No rule is named '{w}'."));
        }
    }

    private static void CheckRegex(string expression, string pointer, List<LoadProblem> problems)
    {
        var problem = RegexSubset.Check(expression);
        if (problem is not null) problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, problem));
    }

    private static void Compile(string? expression, CelContext context, string pointer, List<LoadProblem> problems)
    {
        if (expression is null) return;
        try
        {
            FblCel.Compile(expression, context);
        }
        catch (CelException e)
        {
            problems.Add(new LoadProblem(pointer, ProblemSeverity.Error, e.Message));
        }
    }
}
