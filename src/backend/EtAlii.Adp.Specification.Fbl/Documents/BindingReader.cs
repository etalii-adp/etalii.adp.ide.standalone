using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// Maps a binding's JSON onto the typed records, reporting what it cannot map. Input the schema
/// refuses is reported at its pointer and read as absent, never thrown (FBL §14.1).
/// </summary>
internal static class BindingReader
{
    public static FblBinding? Read(string name, JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A binding is an object."));
            return null;
        }
        if (!json.TryGetProperty("claims", out _)) problems.Add(new LoadDiagnostic(pointer + "/claims", LoadSeverity.Error, "A binding has claims."));
        if (!json.TryGetProperty("body", out _)) problems.Add(new LoadDiagnostic(pointer + "/body", LoadSeverity.Error, "A binding has a body."));
        if (!json.TryGetProperty("reader", out var reader)) problems.Add(new LoadDiagnostic(pointer + "/reader", LoadSeverity.Error, "A binding has a reader."));
        var claims = Object(json, "claims", pointer, "A binding's claims", problems);
        var body = Object(json, "body", pointer, "A binding's body", problems);
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
            Title = Localized(json, "title", pointer, problems),
            Claims = ReadClaims(claims!.Value, pointer + "/claims", problems),
            Body = ReadBody(body!.Value, pointer + "/body", problems),
            Plugin = plugin,
            ReadOnly = ReadOnly(json),
            Text = ReadText(json, pointer, problems),
            Header = Object(json, "header", pointer, "A binding's header", problems) is { } h
                ? new HeaderSettings(JsonProperties.String(h, "key"), h.TryGetProperty("value", out var hv) ? hv.Clone() : null, JsonProperties.String(h, "line"), JsonProperties.Bool(h, "required"))
                : null,
            Comment = JsonProperties.String(json, "comment"),
            ReportUnmatched = JsonProperties.String(json, "unmatched") == "report",
            Blocks = Array(json, "blocks").Select(b => new BlockRule(JsonProperties.String(b, "name") ?? "", JsonProperties.String(b, "line") ?? "", OptionalStrings(b, "within"), JsonProperties.Bool(b, "caseInsensitive"), JsonProperties.String(b, "view"))).ToList(),
            Elements = ReadRules(json, "elements", false, pointer, problems),
            Relations = ReadRules(json, "relations", true, pointer, problems),
            Registration = ReadRegistration(json, pointer, problems),
            Template = Object(json, "template", pointer, "A binding's template", problems) is { } t
                ? new TemplateSettings(JsonProperties.String(t, "text") ?? "", Strings(t, "byOrigin", pointer + "/template", "A template's byOrigin", problems))
                : null,
        };
    }

    private static Claims ReadClaims(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        Marker? marker = null;
        if (Object(json, "marker", pointer, "A claim's marker", problems) is { } m)
        {
            var lines = 20;
            if (m.TryGetProperty("lines", out var l) && !(l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out lines)))
            {
                problems.Add(new LoadDiagnostic(pointer + "/marker/lines", LoadSeverity.Error, "A marker's lines is a whole number."));
                lines = 20;
            }
            marker = new Marker(JsonProperties.String(m, "rootKey"), m.TryGetProperty("value", out var mv) ? mv.Clone() : null, JsonProperties.String(m, "firstLine"), JsonProperties.String(m, "pattern"), lines);
        }
        var readings = new Dictionary<string, ReadingClaim>(StringComparer.Ordinal);
        if (Object(json, "readings", pointer, "A claim's readings", problems) is { } r)
        {
            foreach (var p in r.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object)
                {
                    problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer + "/readings", p.Name), LoadSeverity.Error, "A reading is an object."));
                    continue;
                }
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

    private static TextDefaults ReadText(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "text", pointer, "A binding's text", problems) is not { } t) return new TextDefaults();
        var indent = 2;
        if (t.TryGetProperty("indent", out var i))
        {
            // "tab" is the one indent that is not a number.
            if (i.ValueKind != JsonValueKind.Number) indent = 0;
            else if (!i.TryGetInt32(out indent))
            {
                problems.Add(new LoadDiagnostic(pointer + "/text/indent", LoadSeverity.Error, "A text's indent is a whole number or \"tab\"."));
                indent = 2;
            }
        }
        return new TextDefaults
        {
            Newline = JsonProperties.String(t, "newline") switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" },
            Indent = indent,
            SequenceFlush = JsonProperties.String(t, "sequenceIndent") == "flush",
            Quote = JsonProperties.String(t, "quote") ?? "double",
        };
    }

    private static List<Rule> ReadRules(JsonElement json, string name, bool relation, string pointer, List<LoadDiagnostic> problems)
    {
        var rules = new List<Rule>();
        var index = 0;
        foreach (var item in Array(json, name))
        {
            var at = $"{pointer}/{name}/{index++}";
            if (item.ValueKind == JsonValueKind.Object) rules.Add(ReadRule(item, relation, at, problems));
            else problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A rule is an object."));
        }
        return rules;
    }

    private static Rule ReadRule(JsonElement json, bool relation, string pointer, List<LoadDiagnostic> problems) => new()
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
        Id = ReadId(json, pointer, problems),
        Parent = Object(json, "parent", pointer, "A rule's parent", problems) is { } p ? new ParentBinding(JsonProperties.Strings(p, "rules"), JsonProperties.String(p, "slot")) : null,
        Attributes = ReadAttributes(json, pointer, problems),
        Source = Object(json, "source", pointer, "A relation's source", problems) is { } s ? ReadAttribute(s, pointer + "/source", problems) : null,
        Target = Object(json, "target", pointer, "A relation's target", problems) is { } t ? ReadAttribute(t, pointer + "/target", problems) : null,
        Insert = Object(json, "insert", pointer, "A rule's insert", problems) is { } i ? ReadInsert(i, pointer + "/insert", problems) : null,
        Remove = Object(json, "remove", pointer, "A rule's remove", problems) is { } r ? ReadRemove(r) : null,
        SnapshotUndo = JsonProperties.String(json, "undo") == "snapshot",
        ReadOnly = ReadOnly(json),
    };

    private static IdBinding? ReadId(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "id", pointer, "A rule's id", problems) is not { } id) return null;
        return new IdBinding(
            Object(id, "from", pointer + "/id", "An id's from", problems) is { } from ? ReadSlot(from) : null,
            Object(id, "sidecar", pointer + "/id", "An id's sidecar", problems) is { } sidecar ? JsonProperties.String(sidecar, "key") : null);
    }

    private static List<KeyValuePair<string, AttributeBinding>> ReadAttributes(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var attributes = new List<KeyValuePair<string, AttributeBinding>>();
        if (Object(json, "attributes", pointer, "A rule's attributes", problems) is not { } members) return attributes;
        foreach (var member in members.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer + "/attributes", member.Name);
            if (member.Value.ValueKind == JsonValueKind.Object) attributes.Add(new KeyValuePair<string, AttributeBinding>(member.Name, ReadAttribute(member.Value, at, problems)));
            else problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "An attribute binding is an object."));
        }
        return attributes;
    }

    private static InsertSettings ReadInsert(JsonElement json, string pointer, List<LoadDiagnostic> problems) => new()
    {
        Place = ReadPlace(json, pointer, problems),
        Container = JsonProperties.String(json, "container"),
        Create = Object(json, "create", pointer, "An insert's create", problems) is { } create ? ReadCreateContainer(create, pointer + "/create", problems) : null,
        Keys = JsonProperties.Strings(json, "keys"),
        Emit = JsonProperties.String(json, "emit"),
        Skeleton = JsonProperties.String(json, "skeleton"),
        When = JsonProperties.String(json, "when"),
    };

    /// <summary>An insert's place: its name, or "before" for <c>{before: key}</c>, which is recognised so every family can refuse it; no family places by it yet.</summary>
    private static string ReadPlace(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("place", out var place))
        {
            problems.Add(new LoadDiagnostic(pointer + "/place", LoadSeverity.Error, "An insert names its place."));
            return "end";
        }
        switch (place.ValueKind)
        {
            case JsonValueKind.Object: return "before";
            case JsonValueKind.String: return place.GetString()!;
            default:
                problems.Add(new LoadDiagnostic(pointer + "/place", LoadSeverity.Error, "An insert's place is a name or an object naming the key it goes before."));
                return "end";
        }
    }

    private static CreateContainer? ReadCreateContainer(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var text = JsonProperties.String(json, "text");
        if (json.TryGetProperty("at", out var at))
        {
            if (at.ValueKind == JsonValueKind.String) return new CreateContainer(at.GetString()!, null, text);
            if (at.ValueKind == JsonValueKind.Object && at.EnumerateObject().ToList() is [{ Value.ValueKind: JsonValueKind.String } where, ..])
            {
                return new CreateContainer(where.Name, where.Value.GetString(), text);
            }
        }
        problems.Add(new LoadDiagnostic(pointer + "/at", LoadSeverity.Error, "A container's create says where: a name, or an object naming a key or a selector."));
        return null;
    }

    private static RemoveSettings ReadRemove(JsonElement json)
    {
        var container = JsonProperties.String(json, "container");
        return new RemoveSettings(JsonProperties.Strings(json, "cascade"), container is "remove-when-empty" or "remove-empty-levels", container == "remove-empty-levels");
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

    private static AttributeBinding ReadAttribute(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var slot = ReadSlot(json);
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
            Absent = Strings(json, "absent", pointer, "An attribute's absent", problems),
            Default = json.TryGetProperty("default", out var def) ? def.Clone() : null,
            Decimals = ReadDecimals(json, pointer, problems),
            KeepTimePrecision = JsonProperties.String(json, "time") == "keep-precision",
            Style = JsonProperties.String(json, "style"),
            Reference = Object(json, "reference", pointer, "An attribute's reference", problems) is { } r ? new ReferenceBinding(JsonProperties.Strings(r, "to"), JsonProperties.String(r, "by") ?? "") : null,
            Map = json.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Object
                ? m.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, ScalarText(p.Value))).ToList()
                : null,
            Override = Object(json, "override", pointer, "An attribute's override", problems) is { } o ? ReadSlot(o) : null,
            HtmlParagraphs = JsonProperties.String(json, "content") == "html-paragraphs",
            Create = Object(json, "create", pointer, "An attribute's create", problems) is { } c ? ReadCreateChild(c) : null,
            ReadOnly = ReadOnly(json),
            Read = JsonProperties.String(json, "read"),
            Quoted = JsonProperties.String(json, "quoted"),
            Fallback = json.TryGetProperty("fallback", out var f) && f.ValueKind == JsonValueKind.Object
                ? new FallbackBinding(f.TryGetProperty("value", out var fv) ? fv.Clone() : default, !f.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.False)
                : null,
        };
    }

    /// <summary>The fixed decimals of <c>number: {decimals}</c>; null for <c>shortest</c> or none.</summary>
    private static int? ReadDecimals(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("number", out var n) || n.ValueKind != JsonValueKind.Object || !n.TryGetProperty("decimals", out var d)) return null;
        if (d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var decimals)) return decimals;
        problems.Add(new LoadDiagnostic(pointer + "/number/decimals", LoadSeverity.Error, "A number's decimals is a whole number."));
        return null;
    }

    private static CreateChild ReadCreateChild(JsonElement json)
    {
        var place = json.TryGetProperty("place", out var p) ? p : default;
        if (JsonProperties.String(json, "at") == "end") return new CreateChild("", "last", null, true);
        if (place.ValueKind == JsonValueKind.Object) return new CreateChild(JsonProperties.String(json, "emit") ?? "", "before", JsonProperties.String(place, "before"));
        return new CreateChild(JsonProperties.String(json, "emit") ?? "", place.ValueKind == JsonValueKind.String ? place.GetString()! : "last", null);
    }

    private static RegistrationSettings ReadRegistration(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "registration", pointer, "A binding's registration", problems) is not { } r) return new RegistrationSettings();
        return new RegistrationSettings
        {
            Headers = r.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object ? h.EnumerateObject().Select(p => p.Name).ToList() : [],
            CreateOnFirstPlacement = JsonProperties.Bool(r, "createOnFirstPlacement"),
            ResourceCapture = Object(r, "resource", pointer + "/registration", "A registration's resource", problems) is { } res ? JsonProperties.String(res, "capture") : null,
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

    /// <summary>A human-facing text: a string, or the English, else the first, of a map of strings by language.</summary>
    private static string? Localized(JsonElement json, string name, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Object && v.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String)) return LocalizedValue(v);
        problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer, name), LoadSeverity.Error, $"A {name} is a string or a map of strings by language."));
        return null;
    }

    private static string? LocalizedValue(JsonElement value) =>
        value.TryGetProperty("en", out var en) ? en.GetString() : value.EnumerateObject().Select(p => p.Value.GetString()).FirstOrDefault();

    private static IReadOnlyList<string>? OptionalStrings(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _) ? JsonProperties.Strings(json, name) : null;

    private static IEnumerable<JsonElement> Array(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];

    /// <summary>
    /// The member <paramref name="name"/> when it is an object, else null; one that is there but is not
    /// an object is reported at its pointer as <paramref name="what"/>.
    /// </summary>
    private static JsonElement? Object(JsonElement json, string name, string pointer, string what, List<LoadDiagnostic> problems)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(name, out var member)) return null;
        if (member.ValueKind == JsonValueKind.Object) return member;
        problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer, name), LoadSeverity.Error, $"{what} is an object."));
        return null;
    }

    /// <summary>The object member <paramref name="name"/> as a map of strings; a member that is not a string is reported at its pointer.</summary>
    private static Dictionary<string, string> Strings(JsonElement json, string name, string pointer, string what, List<LoadDiagnostic> problems)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Object(json, name, pointer, what, problems) is not { } members) return map;
        foreach (var member in members.EnumerateObject())
        {
            if (member.Value.ValueKind == JsonValueKind.String) map[member.Name] = member.Value.GetString()!;
            else problems.Add(new LoadDiagnostic(JsonPointer.Append(JsonPointer.Append(pointer, name), member.Name), LoadSeverity.Error, $"{what} holds strings."));
        }
        return map;
    }
}
