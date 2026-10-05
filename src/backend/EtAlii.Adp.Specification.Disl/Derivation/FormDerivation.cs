using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>One row of a property inspector as a host shows it, its id mapped by a <see cref="WireIdMap"/>.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Value">The value as text.</param>
/// <param name="Widget">The item's <c>widget</c>, or null when it states none; the host chooses its editor from it.</param>
/// <param name="ReadOnlyReason">Why it cannot be edited; empty when it can.</param>
/// <param name="Group">The title of the section (or group, or tab) it is in; empty outside one.</param>
/// <param name="Candidates">What the value may be chosen from: a tags or select item's <c>options</c>, a slider's mark labels; null for none.</param>
/// <param name="Attribute">The attribute it edits, or null for a computed item.</param>
/// <param name="Retypes">
/// Whether the row changes the element's type: a computed item with a proposed <c>x-…-retype</c> member,
/// whose <c>options</c> are the types it can become and whose <c>optionLabel</c> names each.
/// </param>
public sealed record DerivedRow(string Id, string Label, string Value, string? Widget, string ReadOnlyReason, string Group, IReadOnlyList<string>? Candidates, string? Attribute, bool Retypes = false);

/// <summary>
/// The property rows of an element (DISL §7.5): the items of the first form <c>for</c> its type whose
/// <c>usage</c> includes <c>inspector</c> (the default), sections flattened into groups, each item
/// or section left out while its <c>visible</c> fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>A row's value</b> is its <c>x-field.display</c> when it has one (the tool's own text, declared as
/// the proposed key), else a computed item's <c>value</c>, else the attribute's text: a stored value as
/// CEL writes it (a <c>yearMonth</c> as <c>uuuu-MM</c>, a list joined by <c>, </c>), an unset one as
/// its default when it has one, else empty.
/// </para>
/// <para>
/// <b>A row's read-only reason</b> is the first of its <c>readOnlyReasons</c> that applies, else, on a
/// read-only diagram, <c>behavior.messages</c>' <c>std.readOnly</c>. A computed item without reasons
/// is the host's to accept or refuse a value for, as its <c>x-field.parse</c> says.
/// </para>
/// <para>
/// <b>A row's id</b> is the map's for the item's <c>x-field.id</c>, else its attribute, else its label.
/// </para>
/// <para>
/// <b>A retype row</b> is a computed item with a proposed <c>x-…-retype</c> member (abm-proposals.md, 2):
/// its candidates are the types the member's <c>options</c> give, each named by its <c>optionLabel</c>
/// with <c>item</c> bound, and the row says <see cref="DerivedRow.Retypes"/> so a host offers a choice.
/// </para>
/// </remarks>
public static class FormDerivation
{
    public static IReadOnlyList<DerivedRow> Derive(DislSpecification specification, DislElement self, DislEnv env, WireIdMap ids, string usage = "inspector")
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(ids);

        var rows = new List<DerivedRow>();
        if (Form(specification, self, usage) is not { } form) return rows;

        var walk = new Walk(specification, self, env, ids, rows);
        walk.Items(form.Json, DislJson.Pointer(form.Pointer, "items"), "");
        return rows;
    }

    private static (JsonElement Json, string Pointer)? Form(DislSpecification specification, DislElement self, string usage)
    {
        foreach (var form in DislJson.Members(specification.Root, "forms"))
        {
            var usages = DislJson.Strings(form.Value, "usage");
            if (usages.Count > 0 ? !usages.Contains(usage) : usage != "inspector") continue;
            if (DislJson.Strings(form.Value, "for").Any(self.IsA)) return (form.Value, DislJson.Pointer("/forms", form.Name));
        }
        return null;
    }

    private sealed class Walk(DislSpecification specification, DislElement self, DislEnv env, WireIdMap ids, List<DerivedRow> rows)
    {
        private readonly CelMap _env = env.ToCel();

        public void Items(JsonElement owner, string pointer, string group)
        {
            if (!owner.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                Item(item, DislJson.Pointer(pointer, index++), group);
            }
        }

        private void Item(JsonElement item, string pointer, string group)
        {
            var kind = DislJson.String(item, "kind") ?? (item.TryGetProperty("attribute", out _) ? "field" : "");
            var attribute = DislJson.String(item, "attribute");
            var variables = Variables(attribute is not null && self.Type.Attributes.ContainsKey(attribute) ? self.ValueOf(attribute) : null);
            if (item.TryGetProperty("visible", out var visible) && !DislEvaluation.Holds(specification, visible, DislJson.Pointer(pointer, "visible"), DislContexts.Form, variables)) return;

            switch (kind)
            {
                case "section" or "group":
                    var titled = item.TryGetProperty("label", out var title) || item.TryGetProperty("title", out title);
                    var name = titled
                        ? DislEvaluation.Message(specification, title, DislJson.Pointer(pointer, item.TryGetProperty("label", out _) ? "label" : "title"), DislContexts.Form, variables, group)
                        : group;
                    Items(item, DislJson.Pointer(pointer, "items"), name);
                    return;
                case "row":
                    Items(item, DislJson.Pointer(pointer, "items"), group);
                    return;
                case "tabs":
                    if (!item.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array) return;
                    var tab = 0;
                    foreach (var each in tabs.EnumerateArray())
                    {
                        var at = DislJson.Pointer(DislJson.Pointer(pointer, "tabs"), tab++);
                        var tabTitle = each.TryGetProperty("title", out var text) ? DislEvaluation.Message(specification, text, DislJson.Pointer(at, "title"), DislContexts.Form, variables, group) : group;
                        Items(each, DislJson.Pointer(at, "items"), tabTitle);
                    }
                    return;
                case "field" or "computed":
                    rows.Add(Row(item, pointer, group, kind, attribute, variables));
                    return;
            }
        }

        private DerivedRow Row(JsonElement item, string pointer, string group, string kind, string? attribute, Dictionary<string, object?> variables)
        {
            var declared = attribute is null ? null : self.Type.Attributes.GetValueOrDefault(attribute);
            var defaultLabel = declared is null ? attribute ?? "" : DislJson.String(declared.Json, "label") ?? declared.Name;
            var label = item.TryGetProperty("label", out var message)
                ? DislEvaluation.Message(specification, message, DislJson.Pointer(pointer, "label"), DislContexts.Form, variables, defaultLabel)
                : defaultLabel;

            string value;
            if (item.TryGetProperty("x-field.display", out var display))
            {
                value = Text(DislEvaluation.Expression(specification, display, DislJson.Pointer(pointer, "x-field.display"), DislContexts.Form, variables));
            }
            else if (kind == "computed")
            {
                value = item.TryGetProperty("value", out var computed)
                    ? computed.ValueKind == JsonValueKind.String ? computed.GetString()! : Text(DislEvaluation.Expression(specification, computed, DislJson.Pointer(pointer, "value"), DislContexts.Form, variables))
                    : "";
            }
            else
            {
                value = declared is null ? "" : AttributeText(declared);
            }

            var reason = item.TryGetProperty("readOnlyReasons", out var reasons)
                ? DislEvaluation.FirstReason(specification, reasons, DislJson.Pointer(pointer, "readOnlyReasons"), DislContexts.Form, variables)
                : null;
            if (reason is null && env.ReadOnly) reason = DislEvaluation.StandardMessage(specification, "std.readOnly", variables, "This diagram is read-only.");

            var key = DislJson.String(item, "x-field.id") ?? attribute ?? label;
            if (kind == "computed" && Retype(item) is { } retype)
            {
                return new DerivedRow(ids.PropertyId(key), label, value, DislJson.String(item, "widget"), reason ?? "", group, Types(retype.Value, DislJson.Pointer(pointer, retype.Name), variables), attribute, Retypes: true);
            }
            return new DerivedRow(ids.PropertyId(key), label, value, DislJson.String(item, "widget"), reason ?? "", group, Candidates(item, pointer, variables), attribute);
        }

        /// <summary>The proposed <c>x-…-retype</c> member of a computed item (abm-proposals.md, 2), or null.</summary>
        private static JsonProperty? Retype(JsonElement item) =>
            item.EnumerateObject().Where(member => member.Name.StartsWith("x-", StringComparison.Ordinal) && member.Name.EndsWith("-retype", StringComparison.Ordinal) && member.Value.ValueKind == JsonValueKind.Object)
                .Select(member => (JsonProperty?)member).FirstOrDefault();

        /// <summary>The types a retype row offers, each named by its <c>optionLabel</c> with <c>item</c> bound, else as itself.</summary>
        private List<string> Types(JsonElement retype, string pointer, Dictionary<string, object?> variables)
        {
            if (!retype.TryGetProperty("options", out var options)
                || DislEvaluation.Expression(specification, options, DislJson.Pointer(pointer, "options"), DislContexts.Form, variables) is not IReadOnlyList<object?> types)
            {
                return [];
            }
            if (!retype.TryGetProperty("optionLabel", out var optionLabel)) return [.. types.Select(Text)];
            return
            [
                .. types.Select(type => Text(DislEvaluation.Expression(
                    specification, optionLabel, DislJson.Pointer(pointer, "optionLabel"), DislContexts.Form, new Dictionary<string, object?>(variables) { ["item"] = type }))),
            ];
        }

        private List<string>? Candidates(JsonElement item, string pointer, Dictionary<string, object?> variables)
        {
            if (item.TryGetProperty("options", out var options))
            {
                switch (options.ValueKind)
                {
                    case JsonValueKind.Array:
                        return [.. options.EnumerateArray().Select(option => option.ValueKind == JsonValueKind.String ? option.GetString()! : DislEvaluation.TextOf(DislValues.Json(option)))];
                    case JsonValueKind.Object when DislJson.String(options, "enum") is { } name:
                        return specification.Metamodel.Enums.TryGetValue(name, out var enumeration) ? [.. enumeration.Values.Select(value => value.Label ?? value.Key)] : [];
                    case JsonValueKind.Object or JsonValueKind.String:
                        return DislEvaluation.Expression(specification, options, DislJson.Pointer(pointer, "options"), DislContexts.Form, variables) is IReadOnlyList<object?> list
                            ? [.. list.Select(Text)]
                            : [];
                }
            }
            if (item.TryGetProperty("widgetOptions", out var widgetOptions) && widgetOptions.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
            {
                return [.. marks.EnumerateArray().Select(mark => DislJson.String(mark, "label") ?? DislEvaluation.TextOf(mark.TryGetProperty("value", out var at) ? DislValues.Json(at) : null))];
            }
            return null;
        }

        /// <summary>An attribute's text: stored, else its default when it declares one, else empty.</summary>
        private string AttributeText(DislAttribute attribute)
        {
            if (!self.Attributes.ContainsKey(attribute.Name) && attribute.Default is null) return "";
            return Format(self.ValueOf(attribute.Name), attribute.Type);
        }

        private static string Format(object? value, string type) => value switch
        {
            long month when type == "yearMonth" => YearMonth.Format(month, "uuuu-MM"),
            IReadOnlyList<object?> list => string.Join(", ", list.Select(item => Format(item, type))),
            _ => DislEvaluation.TextOf(value),
        };

        private static string Text(object? value) => value switch
        {
            CelError => "",
            IReadOnlyList<object?> list => string.Join(", ", list.Select(Text)),
            _ => DislEvaluation.TextOf(value),
        };

        private Dictionary<string, object?> Variables(object? value) =>
            new(StringComparer.Ordinal) { ["self"] = self, ["value"] = value, ["diagram"] = self.Diagram, ["env"] = _env };
    }
}
