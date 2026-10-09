using System.Text.Json;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>One CEL expression of a specification: where it is, its source, its context (§12.3) and the names actions bind around it.</summary>
internal sealed record DislExpressionSite(string Pointer, string Source, string Context, IReadOnlyList<string> Bindings);

/// <summary>
/// Finds every CEL expression of a specification and the context it is evaluated in (DISL §2.5 and
/// §12.3), for step 8 of loading (§14.1). <c>x-</c> extensions (§2.8) and <c>doc</c> objects (§2.4) are
/// not walked: neither is CEL a runtime evaluates. The <c>functions</c> block is compiled by
/// <see cref="UserFunctions"/>, over each function's parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which strings are CEL is the schema's</b>, written out here: the <c>cel</c> member of a Message or
/// Bindable (§2.5 b), the properties typed Expression, every value position of an action but its
/// keywords and names (§2.5 d), and the GeomExprs of shapes (§2.5 c) that are not numbers or
/// percentages.
/// </para>
/// <para>
/// <b>Which context is the property's</b>, per the table of §12.3 and the gesture kinds of §8.4. A CEL
/// string found where this walker names no context is reported, as a warning, and compiled with
/// every variable any context binds: a definition that grows a construct this runtime does not place
/// says so instead of passing unchecked.
/// </para>
/// <para>
/// <b>Not compiled yet</b>: the DISL 0.3 expressions this runtime does not evaluate, because the
/// behaviour they state is code — a ParentPlacement's candidates and rank (<c>behavior.placements</c>),
/// a <c>rows</c> layout's extents and a <c>rowPacked</c> layout's <c>rowsCovered</c>. They are walked
/// as literals, so a mistake in one is not reported at load.
/// </para>
/// </remarks>
internal static partial class DislExpressionWalker
{
    /// <summary>Properties typed Expression wherever they occur (§2.5 a).</summary>
    private static readonly HashSet<string> ExpressionKeys = new(StringComparer.Ordinal)
    {
        "when", "visible", "rule", "keep", "forEach", "enabled", "atMaxDepth", "members",
    };

    /// <summary>The keywords and names of an action, which are never CEL (§2.5 d).</summary>
    private static readonly HashSet<string> ActionNames = new(StringComparer.Ordinal)
    {
        "as", "algorithm", "unset", "call", "plugin", "severity", "form", "place",
    };

    /// <summary>The coordinates a shape states as GeomExprs (§2.5 c, §6.8).</summary>
    private static readonly HashSet<string> GeometryKeys = new(StringComparer.Ordinal)
    {
        "x", "y", "w", "h", "rx", "ry", "x1", "y1", "x2", "y2", "cx", "cy", "r",
    };

    [GeneratedRegex(@"^-?\d+(\.\d+)?%$", RegexOptions.CultureInvariant)]
    private static partial Regex Percentage();

    public static IEnumerable<DislExpressionSite> Sites(JsonElement root)
    {
        var sites = new List<DislExpressionSite>();
        if (root.ValueKind != JsonValueKind.Object) return sites;
        foreach (var section in root.EnumerateObject())
        {
            if (Skipped(section.Name) || section.Name is "functions" or "$schema" or "disl" or "dedl" or "imports" or "plugins") continue;
            var pointer = DislJson.Pointer("", section.Name);
            var frame = section.Name switch
            {
                "language" => Frame.Of(DislContexts.Budget),
                "metamodel" => Frame.Of(DislContexts.Element),
                "coordinates" => Frame.Of(DislContexts.Placement),
                "notation" => Frame.Of(DislContexts.Element),
                "toolbox" => Frame.Of(DislContexts.Element),
                "forms" => Frame.Of(DislContexts.Form),
                "constraints" => Frame.Of(DislContexts.Constraint),
                "behavior" => Frame.Of(DislContexts.Element),
                "layout" => Frame.Of(DislContexts.Element),
                "persistence" => Frame.Of(DislContexts.Element),
                "viewpoints" => Frame.Of(DislContexts.Element),
                _ => Frame.Unknown,
            };
            new Walk(sites, section.Name).Value(section.Value, pointer, frame, [section.Name]);
        }
        return sites;
    }

    private static bool Skipped(string name) => name.StartsWith("x-", StringComparison.Ordinal) || name == "doc";

    /// <summary>What a value is walked as: its context, the names bound around it, and whether a bare string in it is CEL.</summary>
    private sealed record Frame(string? Context, IReadOnlyList<string> Bindings, Mode Mode)
    {
        public static Frame Unknown { get; } = new(null, [], Mode.Literal);

        public static Frame Of(string context) => new(context, [], Mode.Literal);

        public Frame In(string context) => this with { Context = context };

        public Frame As(Mode mode) => this with { Mode = mode };

        public Frame Binding(IEnumerable<string> names) => this with { Bindings = [.. Bindings, .. names] };
    }

    private enum Mode
    {
        /// <summary>A bare string is a literal; only a <c>{cel}</c> object is CEL (§2.5 b).</summary>
        Literal,

        /// <summary>The value is an Expression: a bare string, or the <c>cel</c> of an object (§2.5 a).</summary>
        Expression,

        /// <summary>Inside an action: every value position is an Expression but keywords and names (§2.5 d).</summary>
        Action,

        /// <summary>Inside a shape: coordinates are GeomExprs (§2.5 c).</summary>
        Geometry,
    }

    private sealed class Walk(List<DislExpressionSite> sites, string section)
    {
        public void Value(JsonElement value, string pointer, Frame frame, IReadOnlyList<string> path)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    Object(value, pointer, frame, path);
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Value(item, DislJson.Pointer(pointer, index++), frame, path);
                    }
                    break;
                case JsonValueKind.String:
                    if (frame.Mode is Mode.Expression or Mode.Action) Add(pointer, value.GetString()!, frame);
                    break;
            }
        }

        private void Object(JsonElement value, string pointer, Frame frame, IReadOnlyList<string> path)
        {
            if (value.TryGetProperty("cel", out var cel) && cel.ValueKind == JsonValueKind.String)
            {
                Add(DislJson.Pointer(pointer, "cel"), cel.GetString()!, frame);
            }

            foreach (var property in value.EnumerateObject())
            {
                if (Skipped(property.Name) || property.Name is "cel" or "resultType") continue;
                var next = Next(frame, property.Name, property.Value, value, path);
                if (next is null) continue;
                Value(property.Value, DislJson.Pointer(pointer, property.Name), next, [.. path, property.Name]);
            }
        }

        /// <summary>The frame a property's value is walked in, or null when nothing in it is CEL.</summary>
        private Frame? Next(Frame frame, string key, JsonElement value, JsonElement owner, IReadOnlyList<string> path)
        {
            // Inside an action every value is CEL but the keywords and names.
            if (frame.Mode == Mode.Action)
            {
                // A layout action's refusals are Messages (§9.4), not value positions.
                if (key == "refusals" && path[^1] == "layout") return frame.As(Mode.Literal);
                return ActionNames.Contains(key) || (key == "label" && path[^1] == "editLabel") ? null : frame;
            }

            if (frame.Mode == Mode.Geometry && GeometryKeys.Contains(key) && value.ValueKind == JsonValueKind.String)
            {
                return Percentage().IsMatch(value.GetString()!) ? null : frame.As(Mode.Expression);
            }

            var next = section switch
            {
                "metamodel" => Metamodel(frame, key, value, path),
                "persistence" => Persistence(frame, key, path),
                "constraints" => Constraints(frame, key, owner, path),
                "toolbox" => Toolbox(frame, key, owner, path),
                "forms" => Forms(frame, key, value),
                "behavior" => Behavior(frame, key, path),
                "notation" or "viewpoints" => Notation(frame, key, path),
                "coordinates" => Coordinates(frame, key),
                "language" => key is "order" or "unit" or "measure" && path.Contains("budgets") ? frame.As(Mode.Expression) : frame,
                _ => frame,
            };

            // Action lists, wherever they are: hooks, operations, handle and placement writes, label parses.
            if (key is "actions" or "write" && value.ValueKind == JsonValueKind.Array)
            {
                var context = key == "write" && path.Contains("handles") ? DislContexts.HandleWrite
                    : key == "write" && path.Contains("placement") ? DislContexts.PlacementWrite
                    : next.Context;
                return next.In(context!).Binding(Bound(value)).As(Mode.Action);
            }

            if (ExpressionKeys.Contains(key)) return next.As(Mode.Expression);
            return next;
        }

        private static Frame Metamodel(Frame frame, string key, JsonElement value, IReadOnlyList<string> path)
        {
            // An attribute's derived expression is an Expression in the element context; its CEL default is the create context's.
            if (key == "derived" && path is [.., "attributes", _]) return frame.In(DislContexts.Element).As(Mode.Expression);
            if (key == "default" && path is [.., "attributes", _]) return frame.In(DislContexts.Create).As(Mode.Literal);
            if (key == "display") return frame.In(DislContexts.DataTypeDisplay).As(Mode.Literal);

            // A derived type (§4.11): from is the derive context, every other expression the deriveItem context.
            if (key == "derived" && path is [.., "types" or "relations", _])
            {
                return value.ValueKind == JsonValueKind.String
                    ? frame.In(DislContexts.Derive).As(Mode.Expression)
                    : frame.In(DislContexts.DeriveItem).As(Mode.Literal);
            }
            if (path is [.., "derived"])
            {
                return key switch
                {
                    "from" => frame.In(DislContexts.Derive).As(Mode.Expression),
                    "key" or "id" or "source" or "target" or "owner" or "sources" or "parent" or "slot" => frame.In(DislContexts.DeriveItem).As(Mode.Expression),
                    "attributes" => frame.In(DislContexts.DeriveItem).As(Mode.Expression),
                    "reason" => frame.In(DislContexts.Element).As(Mode.Literal),
                    _ => frame.As(Mode.Literal),
                };
            }

            return frame.As(Mode.Literal);
        }

        private static Frame Persistence(Frame frame, string key, IReadOnlyList<string> path)
        {
            if (path.Contains("ids"))
            {
                // A derived id is the identity context's; a cel id is made on creation, the create context's.
                if (key == "expression") return frame.In(DislContexts.Identity).As(Mode.Expression);
                if (key == "ephemeral") return frame.In(DislContexts.Identity).As(Mode.Expression);
                if (key == "reason") return frame.In(DislContexts.Element).As(Mode.Literal);
            }
            if (key == "migrations") return frame.In(DislContexts.Migration);
            return frame.As(Mode.Literal);
        }

        private static Frame Constraints(Frame frame, string key, JsonElement owner, IReadOnlyList<string> path)
        {
            if (path is ["constraints", "rules"] or [_, "rules"])
            {
                // A rule's own properties: its context follows its kind (§8.4), item and index bound with forEach.
                var context = DislContexts.OfConstraintKind(DislJson.String(owner, "kind"));
                var ruleFrame = frame.In(context);
                if (owner.TryGetProperty("forEach", out _) && key != "forEach") ruleFrame = ruleFrame.Binding(["item", "index"]);
                return key switch
                {
                    "target" or "location" or "subject" => ruleFrame.As(Mode.Expression),
                    // Every other key is literal, fixes included: a fix's when and actions are found by their own keys.
                    _ => ruleFrame.As(Mode.Literal),
                };
            }
            if (path is [_, "builtIn", _])
            {
                return key switch
                {
                    "message" => frame.In(DislContexts.BuiltInMessage).As(Mode.Literal),
                    "refusal" => frame.In(DislContexts.BuiltInRefusal).As(Mode.Literal),
                    "code" => frame.In(DislContexts.BuiltInMessage).As(Mode.Literal),
                    _ => frame.As(Mode.Literal),
                };
            }
            return frame;
        }

        private static Frame Toolbox(Frame frame, string key, JsonElement owner, IReadOnlyList<string> path)
        {
            if (path is [_, "contextMenus", ..])
            {
                if (path.Count == 2)
                {
                    // A set's own when, and its entries: the connection context for a pending connection (§7.3), else the element's.
                    var forConnection = DislJson.Strings(owner, "for").Contains("connection");
                    return frame.In(forConnection ? DislContexts.Connection : DislContexts.Element);
                }
                if (path[^1] == "tools")
                {
                    // An entry generated by forEach sees item (or its as name) and index (§12.3).
                    var named = DislJson.String(owner, "as");
                    var entry = owner.TryGetProperty("forEach", out _) && key != "forEach"
                        ? frame.Binding(named is null ? ["item", "index"] : [named, "item", "index"])
                        : frame;
                    return key == "args" ? entry.As(Mode.Expression) : entry;
                }
                return frame;
            }
            // A palette tool: its initial values in the create context, everything else in the element context on the diagram.
            if (key == "initial") return frame.In(DislContexts.Create).As(Mode.Literal);
            if (key == "refusal") return frame.In(DislContexts.Create).As(Mode.Literal);
            return frame;
        }

        private static Frame Forms(Frame frame, string key, JsonElement value)
        {
            if (key == "initial") return frame.Binding(["position"]).As(value.ValueKind == JsonValueKind.String ? Mode.Expression : Mode.Literal);
            if (key == "confirm") return frame.Binding(["count"]);
            if (key == "options" && value.ValueKind == JsonValueKind.String) return frame.As(Mode.Expression);

            // A type item (DISL 0.3 §7.5): its optionLabel sees each option as item, its refusals the chosen newValue.
            if (key == "optionLabel") return frame.Binding(["item"]).As(Mode.Expression);
            if (key == "refusals") return frame.Binding(["newValue"]);

            // A field's parse (DISL 0.3 §7.5): accepts is an Expression over value; its write is an action list.
            if (key == "accepts") return frame.As(Mode.Expression);
            return frame;
        }

        private static Frame Behavior(Frame frame, string key, IReadOnlyList<string> path)
        {
            if (path.Count == 1)
            {
                return key switch
                {
                    "hooks" => frame.In(DislContexts.Hook),
                    "operations" => frame.In(DislContexts.Operation),
                    "deletion" => frame.In(DislContexts.GestureDelete),
                    "retype" => frame.In(DislContexts.Retype),
                    "simulations" => frame.In(DislContexts.Simulation),
                    "messages" or "reasons" or "editGate" => frame.In(DislContexts.Element),
                    _ => frame,
                };
            }

            // A confirmation sees count besides its context (§9.5); its count is an Expression.
            if (key == "confirm") return frame.Binding(["count"]).As(Mode.Literal);
            if (key == "count" && path[^1] == "confirm") return frame.As(Mode.Expression);
            if (key == "attributeMapping") return frame.As(Mode.Expression);
            return frame;
        }

        private static Frame Notation(Frame frame, string key, IReadOnlyList<string> path)
        {
            if (key == "shapes") return frame.In(DislContexts.Shape).As(Mode.Geometry);
            if (key == "markers") return frame.In(DislContexts.Marker).As(Mode.Geometry);
            if (path.Contains("shapes") || path.Contains("markers"))
            {
                // A handle (§6.8): its position is geometry, its visibility the element's, its snap a snap rule, its write the handleWrite context.
                if (path[^1] == "handles" || path is [.., "handles", _])
                {
                    return key switch
                    {
                        "visible" => frame.In(DislContexts.Element),
                        "snap" => frame.In(DislContexts.HandleSnap).As(Mode.Literal),
                        "label" or "value" or "yValue" => frame.In(DislContexts.Handle).As(Mode.Literal),
                        "refusals" => frame.In(DislContexts.Element).As(Mode.Literal),
                        _ => frame,
                    };
                }
                return frame;
            }
            if (key == "snapping") return frame.In(DislContexts.Snap).As(Mode.Literal);
            if (key == "parse") return frame.In(DislContexts.LabelParse).As(Mode.Literal);
            if (key == "filters") return frame.In(DislContexts.Filter).As(Mode.Literal);
            if (key == "options" && path.Contains("filters")) return frame.In(DislContexts.FilterOptions).As(Mode.Literal);
            if (key == "legend") return frame.In(DislContexts.Legend).As(Mode.Literal);
            if (key is "notices" or "chrome" or "emptyCanvas") return frame.In(DislContexts.Chrome).As(Mode.Literal);
            // A compartment (section 6.9): its items, and everything said per item - its text, its icon,
            // its conditions and, since DISL 0.4, its link and its order - see the item.
            if (key is "items" or "itemText" or "itemIcon" or "itemConditions" or "itemLink" or "itemOrder" && path.Contains("compartments")) return frame.In(DislContexts.CompartmentItem);
            return frame;
        }

        private static Frame Coordinates(Frame frame, string key)
        {
            if (key == "categories") return frame.In(DislContexts.Categories).As(Mode.Expression);
            if (key is "snapping" or "snap") return frame.In(DislContexts.Snap).As(Mode.Literal);
            return frame;
        }

        /// <summary>The names an action list binds (§9.4): every <c>let</c> key and every <c>as</c> name in it.</summary>
        private static IEnumerable<string> Bound(JsonElement actions)
        {
            foreach (var action in actions.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in action.EnumerateObject())
                {
                    if (property is { Name: "as", Value.ValueKind: JsonValueKind.String }) yield return property.Value.GetString()!;
                    if (property is { Name: "let", Value.ValueKind: JsonValueKind.Object })
                    {
                        foreach (var bound in property.Value.EnumerateObject()) yield return bound.Name;
                    }
                    if (property.Name is "actions" or "then" or "else" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nested in Bound(property.Value)) yield return nested;
                    }
                }
            }
        }

        private void Add(string pointer, string source, Frame frame) =>
            sites.Add(new DislExpressionSite(pointer, source, frame.Context ?? "", frame.Bindings));
    }
}
