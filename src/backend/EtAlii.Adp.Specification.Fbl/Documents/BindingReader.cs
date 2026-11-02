using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// Maps a binding's JSON onto the typed records, reporting what it cannot map. Input the schema
/// refuses is reported at its pointer and read as absent, never thrown (FBL §14.1).
/// </summary>
internal static partial class BindingReader
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
        // FBL 0.17 §15.1: what this library does not read of the grammar family is named, and the text it governs kept.
        var grammar = ParseFamily(JsonProperties.String(body!.Value, "family")) == Family.Grammar;
        var unread = grammar ? UnreadConstructs.InBody(body.Value, pointer + "/body", problems) : null;

        PluginReader? plugin = null;
        if (reader.ValueKind == JsonValueKind.Object)
        {
            plugin = new PluginReader(JsonProperties.String(reader, "plugin") ?? "", JsonProperties.String(reader, "version")) { Args = Args(reader, pointer, problems) };
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
            Body = ReadBody(body.Value, pointer + "/body", problems),
            Plugin = plugin,
            ReadOnly = unread ?? ReadOnly(json),
            Text = ReadText(json, pointer, problems),
            Header = Object(json, "header", pointer, "A binding's header", problems) is { } h
                ? new HeaderSettings(JsonProperties.String(h, "key"), h.TryGetProperty("value", out var hv) ? hv.Clone() : null, JsonProperties.String(h, "line"), JsonProperties.Bool(h, "required"))
                : null,
            Comment = JsonProperties.String(json, "comment"),
            ReportUnmatched = JsonProperties.String(json, "unmatched") == "report",
            Blocks = Array(json, "blocks").Select((b, i) => ReadBlockRule(b, $"{pointer}/blocks/{i}", problems)).ToList(),
            Sections = Array(json, "sections").Select(section => new SectionRule(JsonProperties.String(section, "name") ?? "", JsonProperties.String(section, "heading") ?? "", JsonProperties.Bool(section, "caseInsensitive"))).ToList(),
            Elements = ReadRules(json, "elements", false, grammar, pointer, problems),
            Relations = ReadRules(json, "relations", true, grammar, pointer, problems),
            Registration = ReadRegistration(json, pointer, problems),
            Template = Object(json, "template", pointer, "A binding's template", problems) is { } t
                ? new TemplateSettings(JsonProperties.String(t, "text") ?? "", Strings(t, "byOrigin", pointer + "/template", "A template's byOrigin", problems))
                : null,
        };
    }

    /// <summary>A plugin reader's <c>args</c> (FBL §11.1): an object, kept as written for the plugin, which alone knows its keys.</summary>
    private static JsonElement? Args(JsonElement reader, string pointer, List<LoadDiagnostic> problems)
    {
        if (!reader.TryGetProperty("args", out var args)) return null;
        if (args.ValueKind == JsonValueKind.Object) return args.Clone();
        problems.Add(new LoadDiagnostic(pointer + "/reader/args", LoadSeverity.Error, "A reader's args is an object."));
        return null;
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
            Grammar = ReadGrammar(json, pointer, problems),
        };
    }

    /// <summary>
    /// <c>body.grammar</c>, <c>body.start</c> and <c>body.recover</c> (FBL 0.9 §3.2, §4.9); null when the body has no
    /// grammar. The grammar is built once the binding is read, by <see cref="Grammar.GrammarBuild"/>.
    /// </summary>
    private static GrammarBody? ReadGrammar(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("grammar", out var grammar)) return null;
        string? text = null;
        string? file = null;
        if (grammar.ValueKind == JsonValueKind.String) text = grammar.GetString();
        else if (grammar.ValueKind == JsonValueKind.Object && JsonProperties.String(grammar, "file") is { } named) file = named;
        else problems.Add(new LoadDiagnostic(pointer + "/grammar", LoadSeverity.Error, "A body's grammar is its text, or {\"file\": \"<name>.lark\"} beside the FBL document (FBL 0.9 4.9)."));
        GrammarRecover? recover = null;
        if (Object(json, "recover", pointer, "A body's recover", problems) is { } r)
        {
            if (JsonProperties.String(r, "unit") is { } unit)
            {
                recover = new GrammarRecover(unit, JsonProperties.Strings(r, "sync"))
                {
                    Nested = Object(r, "nested", pointer + "/recover", "A recover's nested", problems) is { } nested ? ReadNested(nested, pointer + "/recover/nested", problems) : null,
                    Unclosed = Strings(r, "unclosed", pointer + "/recover", "A recover's unclosed", problems),
                };
            }
            else problems.Add(new LoadDiagnostic(pointer + "/recover/unit", LoadSeverity.Error, "A body's recover names its unit (FBL 0.9 4.9)."));
        }
        return new GrammarBody { Text = text, File = file, Start = JsonProperties.String(json, "start") ?? "start", Recover = recover };
    }

    /// <summary><c>recover.nested</c> (FBL 0.11 §4.9): <c>{"open": o, "close": c}</c>, each a text.</summary>
    private static (string Open, string Close)? ReadNested(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (JsonProperties.String(json, "open") is { Length: > 0 } open && JsonProperties.String(json, "close") is { Length: > 0 } close) return (open, close);
        problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A recover's nested names its open and its close (FBL 0.11 4.9)."));
        return null;
    }

    private static Family? ParseFamily(string? name) => name switch
    {
        "yaml" => Family.Yaml,
        "json" => Family.Json,
        "xml" => Family.Xml,
        "lines" => Family.Lines,
        "blocks" => Family.Blocks,
        "bullets" => Family.Bullets,
        "grammar" => Family.Grammar,
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
            Quote = Setting<QuoteStyle>(t, "quote", pointer + "/text", "A text's quote", problems) ?? QuoteStyle.Double,
        };
    }

    /// <summary>
    /// A block rule (FBL §4.7): its <c>line</c>, or in the grammar family (FBL 0.11 §4.9) its <c>at</c>; a <c>view</c> that is
    /// a group's name, or in the grammar family a slot; and in the grammar family its <c>insert</c> and <c>origins</c>.
    /// </summary>
    private static BlockRule ReadBlockRule(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var view = json.TryGetProperty("view", out var v) ? v : default;
        return new BlockRule(JsonProperties.String(json, "name") ?? "", JsonProperties.String(json, "line") ?? "", OptionalStrings(json, "within"), JsonProperties.Bool(json, "caseInsensitive"),
            view.ValueKind == JsonValueKind.String ? view.GetString() : null)
        {
            At = JsonProperties.String(json, "at"),
            ViewSlot = view.ValueKind == JsonValueKind.Object ? ReadSlot(view, pointer + "/view", problems) : null,
            Insert = Object(json, "insert", pointer, "A block's insert", problems) is { } insert ? ReadInsert(insert, pointer + "/insert", problems) : null,
            Origins = OptionalStrings(json, "origins"),
        };
    }

    private static List<Rule> ReadRules(JsonElement json, string name, bool relation, bool grammar, string pointer, List<LoadDiagnostic> problems)
    {
        var rules = new List<Rule>();
        var index = 0;
        foreach (var item in Array(json, name))
        {
            var at = $"{pointer}/{name}/{index++}";
            if (item.ValueKind == JsonValueKind.Object) rules.Add(ReadRule(item, relation, grammar, at, problems));
            else problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A rule is an object."));
        }
        return rules;
    }

    private static Rule ReadRule(JsonElement json, bool relation, bool grammar, string pointer, List<LoadDiagnostic> problems)
    {
        var type = JsonProperties.String(json, "type") ?? "";
        var unread = grammar ? UnreadConstructs.InRule(json, type, pointer, problems) : null;
        return ReadRule(json, relation, type, unread, pointer, problems);
    }

    private static Rule ReadRule(JsonElement json, bool relation, string type, string? unread, string pointer, List<LoadDiagnostic> problems) => new()
    {
        Name = JsonProperties.String(json, "name") ?? "",
        Type = type,
        IsRelation = relation,
        At = JsonProperties.String(json, "at"),
        Line = JsonProperties.String(json, "line"),
        Within = OptionalStrings(json, "within"),
        // The grammar family's object form (FBL 0.11 §4.9) says the entry may open a block too, and how one is written.
        Opens = JsonProperties.Bool(json, "opens") || (json.TryGetProperty("opens", out var opening) && opening.ValueKind == JsonValueKind.Object),
        OpensWith = json.TryGetProperty("opens", out var opens) && opens.ValueKind == JsonValueKind.Object ? ReadOpens(opens, pointer + "/opens", problems) : null,
        SelectedView = JsonProperties.Bool(json, "selectedView"),
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
        ReadOnly = unread ?? ReadOnly(json),
        Unread = unread,
        Origins = OptionalStrings(json, "origins"),
    };

    /// <summary>A rule's <c>opens</c> in the grammar family (FBL 0.11 §4.9): <c>{"open": " {", "close": "}"}</c>.</summary>
    private static (string Open, string Close)? ReadOpens(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (JsonProperties.String(json, "open") is { Length: > 0 } open && JsonProperties.String(json, "close") is { Length: > 0 } close) return (open, close);
        problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A rule's opens gives its open and its close, each a text (FBL 0.11 4.9)."));
        return null;
    }

    private static IdBinding? ReadId(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "id", pointer, "A rule's id", problems) is not { } id) return null;
        var from = Object(id, "from", pointer + "/id", "An id's from", problems) is { } slot ? ReadSlot(slot, pointer + "/id/from", problems) : null;
        return Object(id, "sidecar", pointer + "/id", "An id's sidecar", problems) is not { } sidecar
            ? new IdBinding(from, null)
            : new IdBinding(from, JsonProperties.String(sidecar, "key"), JsonProperties.String(sidecar, "kind"), JsonProperties.String(sidecar, "ordinal"));
    }

    private static List<KeyValuePair<string, AttributeBinding>> ReadAttributes(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var attributes = new List<KeyValuePair<string, AttributeBinding>>();
        if (Object(json, "attributes", pointer, "A rule's attributes", problems) is not { } members) return attributes;
        foreach (var member in members.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer + "/attributes", member.Name);
            if (member.Value.ValueKind != JsonValueKind.Object) problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "An attribute binding is an object."));
            else if (ReadAttribute(member.Value, at, problems) is { } attribute) attributes.Add(new KeyValuePair<string, AttributeBinding>(member.Name, attribute));
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
        Constant = Object(json, "constant", pointer, "An insert's constant", problems) is { } constant ? ReadConstant(constant, pointer + "/constant", problems) : null,
        WhenRefusal = Refusal(json, "when", pointer, problems),
        ContainerRefusal = Refusal(json, "container", pointer, problems),
        KeyRefusal = Refusal(json, "key", pointer, problems),
        DuplicateRefusal = Refusal(json, "duplicate", pointer, problems),
    };

    /// <summary>
    /// The sentence of <c>refusals.<paramref name="name"/></c> (FBL 0.10 §5.2, §6.2): a LocalizedText, in English where
    /// it gives one; null when the binding gives none, and the host words the refusal.
    /// </summary>
    private static string? Refusal(JsonElement json, string name, string pointer, List<LoadDiagnostic> problems) =>
        Object(json, "refusals", pointer, "A refusals", problems) is { } refusals ? Localized(refusals, name, pointer + "/refusals", problems) : null;

    /// <summary>An insert's constant (FBL 0.8 §6.2): each member a scalar, kept in member order; a member that is not one is reported at its pointer.</summary>
    private static List<KeyValuePair<string, object?>> ReadConstant(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var constant = new List<KeyValuePair<string, object?>>();
        foreach (var member in json.EnumerateObject())
        {
            object? value = member.Value.ValueKind switch
            {
                JsonValueKind.String => member.Value.GetString(),
                JsonValueKind.Number => member.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
            if (member.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer, member.Name), LoadSeverity.Error, "A constant's value is a string, a number, a boolean or null."));
            }
            else constant.Add(new KeyValuePair<string, object?>(member.Name, value));
        }
        return constant;
    }

    /// <summary>An insert's place: its name, or "before" for <c>{before: key}</c>, which is recognised so every family can refuse it; no family places by it yet.</summary>
    private static InsertPlace ReadPlace(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("place", out var place))
        {
            problems.Add(new LoadDiagnostic(pointer + "/place", LoadSeverity.Error, "An insert names its place."));
            return InsertPlace.End;
        }
        if (place.ValueKind == JsonValueKind.Object) return InsertPlace.Before;
        if (place.ValueKind == JsonValueKind.String && place.GetString() != WireName.Of(InsertPlace.Before) && WireName.TryParse<InsertPlace>(place.GetString(), out var named)) return named;
        problems.Add(new LoadDiagnostic(pointer + "/place", LoadSeverity.Error, "An insert's place is a name or an object naming the key it goes before."));
        return InsertPlace.End;
    }

    private static CreateContainer? ReadCreateContainer(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var text = JsonProperties.String(json, "text");
        if (json.TryGetProperty("at", out var at))
        {
            if (at.ValueKind == JsonValueKind.String && WireName.TryParse<ContainerPlace>(at.GetString(), out var named)) return new CreateContainer(named, null, text);
            if (at.ValueKind == JsonValueKind.Object && at.EnumerateObject().ToList() is [{ Value.ValueKind: JsonValueKind.String } where, ..] && WireName.TryParse<ContainerPlace>(where.Name, out var keyed))
            {
                return new CreateContainer(keyed, where.Value.GetString(), text);
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

    private static Slot? ReadSlot(JsonElement json, string pointer, List<LoadDiagnostic> problems) =>
        ReadKind(json, pointer, "A slot names exactly one place (FBL §3.3).", problems) is { } kind
            ? new Slot
            {
                Kind = kind, Child = JsonProperties.String(json, "child"), Word = JsonProperties.String(json, "word"), Flag = JsonProperties.Bool(json, "flag"), Match = JsonProperties.String(json, "match"),
                Escape = ReadEscape(json, pointer, problems), Split = ReadSplit(json, pointer, problems),
            }
            : null;

    /// <summary>An attribute's <c>gather</c> (FBL 0.11 §5.2): further <c>{node}</c> slots of the same entry, each with its <c>match</c>, <c>escape</c> and <c>split</c>.</summary>
    private static List<Slot> ReadGather(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var gather = new List<Slot>();
        if (!json.TryGetProperty("gather", out var slots)) return gather;
        if (slots.ValueKind != JsonValueKind.Array)
        {
            problems.Add(new LoadDiagnostic(pointer + "/gather", LoadSeverity.Error, "An attribute's gather is a list of slots (FBL 0.11 5.2)."));
            return gather;
        }
        var index = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            var at = $"{pointer}/gather/{index++}";
            if (slot.ValueKind != JsonValueKind.Object) problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A gathered slot is an object."));
            else if (ReadSlot(slot, at, problems) is { } read) gather.Add(read);
        }
        return gather;
    }

    /// <summary>An attribute's <c>or</c> (FBL 0.15 §4.9): further slots of the same entry, each with its <c>match</c>, <c>escape</c> and <c>split</c>.</summary>
    private static List<Slot> ReadFurther(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var further = new List<Slot>();
        if (!json.TryGetProperty("or", out var slots)) return further;
        if (slots.ValueKind != JsonValueKind.Array)
        {
            problems.Add(new LoadDiagnostic(pointer + "/or", LoadSeverity.Error, "An attribute's or is a list of slots (FBL 0.15 4.9)."));
            return further;
        }
        var index = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            var at = $"{pointer}/or/{index++}";
            if (slot.ValueKind != JsonValueKind.Object) problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A further slot is an object."));
            else if (ReadSlot(slot, at, problems) is { } read) further.Add(read);
        }
        return further;
    }

    /// <summary>A slot's <c>items</c> (FBL 0.12 §5.2): an object from each item attribute to the slot that reads it; null when it has none.</summary>
    private static List<KeyValuePair<string, Slot>>? ReadItems(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("items", out var items)) return null;
        var read = new List<KeyValuePair<string, Slot>>();
        if (items.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new LoadDiagnostic(pointer + "/items", LoadSeverity.Error, "A slot's items is an object from each item attribute to its slot (FBL 0.12 5.2)."));
            return read;
        }
        foreach (var item in items.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer + "/items", item.Name);
            if (item.Value.ValueKind != JsonValueKind.Object) problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "An item attribute's slot is an object."));
            else if (ReadSlot(item.Value, at, problems) is { } slot) read.Add(new KeyValuePair<string, Slot>(item.Name, slot));
        }
        return read;
    }

    /// <summary>The one kind of slot <paramref name="json"/> names; none or several are reported as <paramref name="problem"/>.</summary>
    private static SlotKind? ReadKind(JsonElement json, string pointer, string problem, List<LoadDiagnostic> problems)
    {
        List<SlotKind> kinds = [];
        if (JsonProperties.String(json, "key") is { } key) kinds.Add(new SlotKind.Key(key));
        if (JsonProperties.String(json, "attribute") is { } attribute) kinds.Add(new SlotKind.XmlAttribute(attribute));
        if (JsonProperties.Bool(json, "text")) kinds.Add(new SlotKind.Text());
        if (JsonProperties.String(json, "group") is { } group) kinds.Add(new SlotKind.Group(group));
        if (JsonProperties.String(json, "parent") is { } parent) kinds.Add(new SlotKind.Parent(parent));
        if (JsonProperties.String(json, "capture") is { } capture) kinds.Add(new SlotKind.Capture(capture));
        if (JsonProperties.String(json, "value") is { } value) kinds.Add(new SlotKind.Value(value));
        if (JsonProperties.Bool(json, "continuation")) kinds.Add(new SlotKind.Continuation());
        if (JsonProperties.String(json, "node") is { } node) kinds.Add(new SlotKind.Node(node));
        if (kinds is [var kind]) return kind;
        problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, problem));
        return null;
    }

    private static AttributeBinding? ReadAttribute(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (ReadKind(json, pointer, "An attribute binding names exactly one slot (FBL §3.3).", problems) is not { } kind) return null;
        return new AttributeBinding
        {
            Kind = kind,
            Child = JsonProperties.String(json, "child"),
            Word = JsonProperties.String(json, "word"),
            Flag = JsonProperties.Bool(json, "flag"),
            Match = JsonProperties.String(json, "match"),
            Escape = ReadEscape(json, pointer, problems),
            Split = ReadSplit(json, pointer, problems),
            Gather = ReadGather(json, pointer, problems),
            Or = ReadFurther(json, pointer, problems),
            Separator = JsonProperties.String(json, "separator"),
            Items = ReadItems(json, pointer, problems),
            Empty = Setting<EmptyRule>(json, "empty", pointer, "An attribute's empty", problems) ?? EmptyRule.Keep,
            Absent = ReadAbsent(json, pointer, problems),
            Default = json.TryGetProperty("default", out var def) ? def.Clone() : null,
            Decimals = ReadDecimals(json, pointer, problems),
            KeepTimePrecision = JsonProperties.String(json, "time") == "keep-precision",
            Style = Setting<WrittenStyle>(json, "style", pointer, "An attribute's style", problems),
            Reference = Object(json, "reference", pointer, "An attribute's reference", problems) is { } r ? new ReferenceBinding(JsonProperties.Strings(r, "to"), JsonProperties.String(r, "by") ?? "") : null,
            Map = json.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Object
                ? m.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, ScalarText(p.Value))).ToList()
                : null,
            Override = Object(json, "override", pointer, "An attribute's override", problems) is { } o ? ReadSlot(o, pointer + "/override", problems) : null,
            HtmlParagraphs = JsonProperties.String(json, "content") == "html-paragraphs",
            Create = Object(json, "create", pointer, "An attribute's create", problems) is { } c ? ReadCreateChild(c, pointer + "/create", problems) : null,
            ReadOnly = ReadOnly(json),
            Read = Setting<ReadAs>(json, "read", pointer, "An attribute's read", problems),
            Quoted = Setting<QuotedRule>(json, "quoted", pointer, "An attribute's quoted", problems),
            Fallback = json.TryGetProperty("fallback", out var f) && f.ValueKind == JsonValueKind.Object
                ? new FallbackBinding(f.TryGetProperty("value", out var fv) ? fv.Clone() : default, !f.TryGetProperty("report", out var report) || report.ValueKind != JsonValueKind.False)
                : null,
            Secondary = Object(json, "secondary", pointer, "An attribute's secondary", problems) is { } s ? ReadSlot(s, pointer + "/secondary", problems) : null,
            EmptyRefusal = Refusal(json, "empty", pointer, problems),
            AbsentRefusal = Refusal(json, "absent", pointer, problems),
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

    private static CreateChild ReadCreateChild(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var place = json.TryGetProperty("place", out var p) ? p : default;
        if (JsonProperties.String(json, "at") == "end") return new CreateChild("", ChildPlace.Last, null, true);
        var after = json.TryGetProperty("after", out var a) && a.ValueKind == JsonValueKind.Array ? JsonProperties.Strings(json, "after") : null;
        var pad = JsonProperties.Bool(json, "pad");
        return place.ValueKind == JsonValueKind.Object
            ? new CreateChild(JsonProperties.String(json, "emit") ?? "", ChildPlace.Before, JsonProperties.String(place, "before")) { After = after, Pad = pad }
            : new CreateChild(JsonProperties.String(json, "emit") ?? "", Setting<ChildPlace>(json, "place", pointer, "A child's place", problems) ?? ChildPlace.Last, null) { After = after, Pad = pad };
    }

    /// <summary>
    /// A slot's <c>escape</c> (FBL 0.11 §5.2), <c>{"with": "\\", "quote": "\""}</c>, one character each, and its
    /// <c>requote</c> (FBL 0.15 §5.2), one character too when it is given.
    /// </summary>
    private static SlotEscape? ReadEscape(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "escape", pointer, "A slot's escape", problems) is not { } escape) return null;
        if (JsonProperties.String(escape, "with") is not { Length: 1 } with || JsonProperties.String(escape, "quote") is not { Length: 1 } quote)
        {
            problems.Add(new LoadDiagnostic(pointer + "/escape", LoadSeverity.Error, "A slot's escape gives 'with' and 'quote', one character each (FBL 0.11 5.2)."));
            return null;
        }
        if (!escape.TryGetProperty("requote", out _)) return new SlotEscape(with[0], quote[0]);
        if (JsonProperties.String(escape, "requote") is { Length: 1 } requote) return new SlotEscape(with[0], quote[0]) { Requote = requote[0] };
        problems.Add(new LoadDiagnostic(pointer + "/escape", LoadSeverity.Error, "A slot's escape gives 'requote' as one character (FBL 0.15 5.2)."));
        return null;
    }

    /// <summary>A slot's <c>split</c> (FBL 0.13 §4.9): a text of one character or more, never beside <c>items</c>.</summary>
    private static string? ReadSplit(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (!json.TryGetProperty("split", out var split)) return null;
        if (split.ValueKind != JsonValueKind.String || split.GetString() is not { Length: > 0 } text)
        {
            problems.Add(new LoadDiagnostic(pointer + "/split", LoadSeverity.Error, "A slot's split is a text of one character or more (FBL 0.13 4.9)."));
            return null;
        }
        if (json.TryGetProperty("items", out _)) problems.Add(new LoadDiagnostic(pointer + "/split", LoadSeverity.Error, "A slot's split never stands beside items (FBL 0.13 14.1)."));
        return text;
    }

    /// <summary>
    /// The setting <paramref name="name"/> as its enumeration, or null when it is not given; a value
    /// that names none of its cases is reported at its pointer as <paramref name="what"/>.
    /// </summary>
    private static T? Setting<T>(JsonElement json, string name, string pointer, string what, List<LoadDiagnostic> problems) where T : struct, Enum
    {
        if (!json.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String && WireName.TryParse<T>(value.GetString(), out var setting)) return setting;
        var names = string.Join(", ", Enum.GetValues<T>().Select(c => $"\"{WireName.Of(c)}\""));
        problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer, name), LoadSeverity.Error, $"{what} is one of {names}."));
        return null;
    }

    /// <summary>An attribute's <c>absent</c>: per family, what setting a value whose slot is absent does.</summary>
    private static Dictionary<string, AbsentRule> ReadAbsent(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        var absent = new Dictionary<string, AbsentRule>(StringComparer.Ordinal);
        if (Object(json, "absent", pointer, "An attribute's absent", problems) is not { } members) return absent;
        foreach (var member in members.EnumerateObject())
        {
            if (Setting<AbsentRule>(members, member.Name, pointer + "/absent", "An attribute's absent", problems) is { } rule) absent[member.Name] = rule;
        }
        return absent;
    }

    private static RegistrationSettings ReadRegistration(JsonElement json, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(json, "registration", pointer, "A binding's registration", problems) is not { } r) return new RegistrationSettings();
        return new RegistrationSettings
        {
            Headers = r.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object ? h.EnumerateObject().Select(p => p.Name).ToList() : [],
            CreateOnFirstPlacement = JsonProperties.Bool(r, "createOnFirstPlacement"),
            Blocks = ReadBlocks(r, pointer + "/registration", problems),
            ResourceCapture = Object(r, "resource", pointer + "/registration", "A registration's resource", problems) is { } res ? JsonProperties.String(res, "capture") : null,
            ResourceByOrigin = Object(r, "resource", pointer + "/registration", "A registration's resource", problems) is { } resource ? ReadByOrigin(resource, pointer + "/registration/resource", problems) : null,
            LegacyLayout = JsonProperties.String(r, "legacyLayout"),
            LegacyIdentities = JsonProperties.String(r, "legacyIdentities"),
        };
    }

    /// <summary>
    /// <c>registration.resource.byOrigin</c> (FBL 0.10 §8.2): per origin, the capture its reading selects by, or null
    /// for one that selects none; null when the resource gives no <c>byOrigin</c>.
    /// </summary>
    private static Dictionary<string, string?>? ReadByOrigin(JsonElement resource, string pointer, List<LoadDiagnostic> problems)
    {
        if (Object(resource, "byOrigin", pointer, "A resource's byOrigin", problems) is not { } byOrigin) return null;
        var captures = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var origin in byOrigin.EnumerateObject())
        {
            if (origin.Value.ValueKind == JsonValueKind.Null) captures[origin.Name] = null;
            else if (origin.Value.ValueKind == JsonValueKind.Object && JsonProperties.String(origin.Value, "capture") is { } capture) captures[origin.Name] = capture;
            else problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer + "/byOrigin", origin.Name), LoadSeverity.Error, "A byOrigin entry is null or an object naming a capture."));
        }
        return captures;
    }

    /// <summary>A Name (FBL §2.2).</summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockName();

    /// <summary>A registration's declared blocks (FBL 0.7 §8.1): each a Name, with its order, its keys and whether it is in the history.</summary>
    private static List<RegistrationBlockDeclaration> ReadBlocks(JsonElement registration, string pointer, List<LoadDiagnostic> problems)
    {
        var blocks = new List<RegistrationBlockDeclaration>();
        if (Object(registration, "blocks", pointer, "A registration's blocks", problems) is not { } declared) return blocks;
        foreach (var block in declared.EnumerateObject())
        {
            var at = JsonPointer.Append(pointer + "/blocks", block.Name);
            if (block.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A declared block is an object."));
                continue;
            }
            if (!BlockName().IsMatch(block.Name) || block.Name is "layout" or "identities")
            {
                problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, $"'{block.Name}' cannot name a declared block: it is a Name, and neither layout nor identities (FBL 8.1)."));
                continue;
            }
            var order = JsonProperties.String(block.Value, "order") ?? "ordinal";
            if (order is not ("ordinal" or "kept")) problems.Add(new LoadDiagnostic(at + "/order", LoadSeverity.Error, "A block's order is \"ordinal\" or \"kept\"."));
            var keys = JsonProperties.String(block.Value, "keys") ?? "names";
            if (keys is not ("names" or "ids")) problems.Add(new LoadDiagnostic(at + "/keys", LoadSeverity.Error, "A block's keys are \"names\" or \"ids\"."));
            var history = !block.Value.TryGetProperty("history", out var h) || h.ValueKind != JsonValueKind.False;
            blocks.Add(new RegistrationBlockDeclaration(block.Name, order == "kept", keys == "ids", history));
        }
        return blocks;
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
