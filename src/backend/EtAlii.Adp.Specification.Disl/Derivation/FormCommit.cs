using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>A dialog a params form asks a host to show (DISL 0.3 §7.5): its title, icon, confirming label and fields.</summary>
/// <param name="Label">The form's <c>label</c>.</param>
/// <param name="Icon">The form's <c>icon</c>, or null when it states none.</param>
/// <param name="SubmitLabel">The form's <c>submitLabel</c>, or null for the runtime's own.</param>
/// <param name="Fields">The form's items, in order.</param>
public sealed record FormRequest(string Label, string? Icon, string? SubmitLabel, IReadOnlyList<FormRequestField> Fields);

/// <summary>One field of a <see cref="FormRequest"/>: the parameter it answers, its label, its widget and the value it opens with.</summary>
public sealed record FormRequestField(string Attribute, string Label, string? Widget, object? Initial);

/// <summary>
/// A form item at commit (DISL 0.3 §7.5): what an inspector row does with the value typed into it, and what a params
/// form asks for and refuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>A row's commit</b> runs in the order §7.5 gives: the row's read-only reasons (4.3), or on a read-only diagram
/// <c>std.readOnly</c>; then each <c>validate</c> of severity <c>error</c>, whatever its timing, since a commit is when
/// both kinds are judged; then the <c>parse</c>, whose <c>accepts</c> refuses with its <c>refusal</c> and whose
/// <c>write</c> makes the transaction. A row without a <c>parse</c> sets its attribute to the value, typed by the
/// attribute; a computed row without one cannot be committed.
/// </para>
/// <para>
/// <b>A choice is made by its label.</b> A row whose <c>options</c> are <c>{value, label}</c> maps receives the label a
/// host shows; the option of that label gives the value <c>validate</c> sees and the row writes. A text no option is
/// labelled with is kept as text, for <c>validate</c> to judge.
/// </para>
/// <para>
/// <b>The element's diagram is the transaction's working state</b>: a <c>write</c> is run on it as an operation's
/// actions are, so a host gives this a diagram of its own and drops it when the transaction is refused.
/// </para>
/// </remarks>
public static class FormCommit
{
    /// <summary>
    /// What committing <paramref name="value"/> to the row <paramref name="row"/> of <paramref name="self"/>'s form does:
    /// refused with the sentence the definition gives, or the transaction to write. The row is named as
    /// <see cref="FormDerivation"/> names it: by its item's <c>id</c>, else its attribute, else its label.
    /// </summary>
    public static DislTransaction Row(DislSpecification specification, DislElement self, string row, string value, DislEnv env, string usage = "inspector")
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(env);

        if (FormOf(specification, self, usage) is not { } form || Find(form.Json, DislJson.Pointer(form.Pointer, "items"), row) is not { } found)
        {
            return DislTransaction.Refused($"'{row}' is not a row of this element's form.");
        }

        (JsonElement item, string pointer) = found;
        var attribute = DislJson.String(item, "attribute");
        var declared = attribute is null ? null : self.Type.Attributes.GetValueOrDefault(attribute);
        var cel = env.ToCel();
        var current = Variables(self, declared is null ? null : self.ValueOf(declared.Name), cel);

        if (item.TryGetProperty("readOnlyReasons", out var reasons)
            && DislEvaluation.FirstReason(specification, reasons, DislJson.Pointer(pointer, "readOnlyReasons"), DislContexts.Form, current) is { } reason)
        {
            return DislTransaction.Refused(reason);
        }
        if (env.ReadOnly) return DislTransaction.Refused(DislEvaluation.StandardMessage(specification, "std.readOnly", current, "This diagram is read-only."));

        var chosen = Chosen(specification, item, pointer, current, value);
        if (Invalid(specification, item, pointer, Variables(self, chosen, cel)) is { } invalid) return DislTransaction.Refused(invalid);

        if (item.TryGetProperty("parse", out var parse))
        {
            var text = Variables(self, value, cel);
            var parsePointer = DislJson.Pointer(pointer, "parse");
            if (parse.TryGetProperty("accepts", out var accepts) && !DislEvaluation.Holds(specification, accepts, DislJson.Pointer(parsePointer, "accepts"), DislContexts.Form, text))
            {
                return DislTransaction.Refused(parse.TryGetProperty("refusal", out var refusal)
                    ? DislEvaluation.Message(specification, refusal, DislJson.Pointer(parsePointer, "refusal"), DislContexts.Form, text, $"'{value}' cannot be written here.")
                    : $"'{value}' cannot be written here.");
            }

            var runner = new ActionRunner(specification, self.Diagram, DislIds.Fixed(), DislContexts.Form);
            if (parse.TryGetProperty("write", out var write)) runner.Run(write, DislJson.Pointer(parsePointer, "write"), text);
            return runner.Transaction;
        }

        if (declared is null || DislJson.String(item, "kind") == "computed")
        {
            return DislTransaction.Refused($"'{row}' cannot be edited here.");
        }

        object? written = chosen;
        if (chosen is string typed && !DislValues.TryFromRead(typed, declared, specification.Metamodel, out written))
        {
            return DislTransaction.Refused($"'{value}' is not a value of {declared.Name}.");
        }
        return new DislTransaction([new DislChange.Set(self.Id, self.Type.Name, new Dictionary<string, object?>(StringComparer.Ordinal) { [declared.Name] = DislWrite.StoredForm(specification, self.Type.Name, declared.Name, written) })], [], null);
    }

    /// <summary>
    /// The dialog the params form <paramref name="form"/> asks a host to show for <paramref name="self"/> (null for a
    /// form on the diagram): its label, icon and <c>submitLabel</c>, and each item's label, widget and <c>initial</c>
    /// value, evaluated once; null when there is no such form.
    /// </summary>
    public static FormRequest? Request(DislSpecification specification, string form, DislElement? self, DislEnv env)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(env);

        if (!specification.Root.TryGetProperty("forms", out var forms) || !forms.TryGetProperty(form, out var json)) return null;
        var pointer = DislJson.Pointer("/forms", form);
        var variables = Variables(self, null, env.ToCel(), self?.Diagram);

        var fields = new List<FormRequestField>();
        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                var at = DislJson.Pointer(DislJson.Pointer(pointer, "items"), index++);
                var attribute = DislJson.String(item, "attribute") ?? "";
                var label = item.TryGetProperty("label", out var text) ? DislEvaluation.Message(specification, text, DislJson.Pointer(at, "label"), DislContexts.Form, variables, attribute) : attribute;
                var initial = item.TryGetProperty("initial", out var expression) ? DislEvaluation.Expression(specification, expression, DislJson.Pointer(at, "initial"), DislContexts.Form, variables) : null;
                fields.Add(new FormRequestField(attribute, label, DislJson.String(item, "widget"), initial is CelError ? null : initial));
            }
        }

        return new FormRequest(
            json.TryGetProperty("label", out var title) ? DislEvaluation.Message(specification, title, DislJson.Pointer(pointer, "label"), DislContexts.Form, variables, form) : form,
            DislJson.String(json, "icon"),
            json.TryGetProperty("submitLabel", out var submit) ? DislEvaluation.Message(specification, submit, DislJson.Pointer(pointer, "submitLabel"), DislContexts.Form, variables, "") : null,
            fields);
    }

    /// <summary>
    /// Why the params form <paramref name="form"/> refuses <paramref name="value"/> for its item
    /// <paramref name="attribute"/>: the message of the first of its validations of severity <c>error</c> that fails;
    /// null when the value is accepted.
    /// </summary>
    public static string? Refusal(DislSpecification specification, string form, string attribute, object? value, DislElement? self, DislEnv env)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(env);

        if (!specification.Root.TryGetProperty("forms", out var forms) || !forms.TryGetProperty(form, out var json)
            || !json.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var at = DislJson.Pointer(DislJson.Pointer(DislJson.Pointer("/forms", form), "items"), index++);
            if (DislJson.String(item, "attribute") != attribute) continue;
            return Invalid(specification, item, at, Variables(self, value, env.ToCel(), self?.Diagram));
        }
        return null;
    }

    /// <summary>
    /// What committing the inline edit <paramref name="text"/> of <paramref name="self"/>'s label
    /// <paramref name="label"/> does (DISL 0.3 §6.12): on a read-only diagram <c>std.readOnly</c>; then the label's
    /// <c>parse</c>, whose <c>accepts</c> refuses with its <c>refusal</c> and whose <c>write</c> makes the
    /// transaction. A label without a <c>parse</c>, or with none of that id, cannot be edited.
    /// </summary>
    /// <remarks>
    /// The refusal is the label's editor's alone: a form item on the same attribute keeps its own validation (§6.12).
    /// </remarks>
    public static DislTransaction Label(DislSpecification specification, DislElement self, string label, string text, DislEnv env)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(env);

        var variables = Variables(self, text, env.ToCel());
        if (env.ReadOnly) return DislTransaction.Refused(DislEvaluation.StandardMessage(specification, "std.readOnly", variables, "This diagram is read-only."));
        if (LabelOf(specification, self, label) is not { } found || !found.Json.TryGetProperty("parse", out var parse))
        {
            return DislTransaction.Refused($"'{label}' cannot be edited here.");
        }

        var parsePointer = DislJson.Pointer(found.Pointer, "parse");
        if (parse.TryGetProperty("accepts", out var accepts) && !DislEvaluation.Holds(specification, accepts, DislJson.Pointer(parsePointer, "accepts"), DislContexts.LabelParse, variables))
        {
            return DislTransaction.Refused(parse.TryGetProperty("refusal", out var refusal)
                ? DislEvaluation.Message(specification, refusal, DislJson.Pointer(parsePointer, "refusal"), DislContexts.LabelParse, variables, $"'{text}' cannot be written here.")
                : $"'{text}' cannot be written here.");
        }

        var runner = new ActionRunner(specification, self.Diagram, DislIds.Fixed(), DislContexts.LabelParse);
        if (parse.TryGetProperty("write", out var write)) runner.Run(write, DislJson.Pointer(parsePointer, "write"), variables);
        return runner.Transaction;
    }

    /// <summary>
    /// The text the inline editor of <paramref name="self"/>'s label <paramref name="label"/> opens on (DISL §6.12): its
    /// <c>editText</c>, else its <c>text</c>; null when there is no such label or it does not evaluate to text.
    /// </summary>
    public static string? EditText(DislSpecification specification, DislElement self, string label, DislEnv env)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(env);

        if (LabelOf(specification, self, label) is not { } found) return null;
        var key = found.Json.TryGetProperty("editText", out _) ? "editText" : "text";
        if (!found.Json.TryGetProperty(key, out var json)) return null;
        return DislEvaluation.Expression(specification, json, DislJson.Pointer(found.Pointer, key), DislContexts.Element, Variables(self, null, env.ToCel())) as string;
    }

    /// <summary>The label <paramref name="id"/> of the notation of <paramref name="self"/>'s type: a node's, else a relation's.</summary>
    private static (JsonElement Json, string Pointer)? LabelOf(DislSpecification specification, DislElement self, string id)
    {
        if (!specification.Root.TryGetProperty("notation", out var notation)) return null;
        foreach (var kind in new[] { "nodes", "edges" })
        {
            foreach (var type in DislJson.Members(notation, kind))
            {
                if (type.Name != self.Type.Name || !type.Value.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) continue;
                var index = 0;
                foreach (var label in labels.EnumerateArray())
                {
                    var at = DislJson.Pointer(DislJson.Pointer(DislJson.Pointer(DislJson.Pointer("/notation", kind), type.Name), "labels"), index++);
                    if (DislJson.String(label, "id") == id) return (label, at);
                }
            }
        }
        return null;
    }

    /// <summary>The message of the first validation of severity <c>error</c> of <paramref name="item"/> that fails, or null.</summary>
    private static string? Invalid(DislSpecification specification, JsonElement item, string pointer, Dictionary<string, object?> variables)
    {
        if (!item.TryGetProperty("validate", out var validations) || validations.ValueKind != JsonValueKind.Array) return null;
        var index = 0;
        foreach (var validation in validations.EnumerateArray())
        {
            var at = DislJson.Pointer(DislJson.Pointer(pointer, "validate"), index++);
            if ((DislJson.String(validation, "severity") ?? "error") != "error" || !validation.TryGetProperty("rule", out var rule)) continue;
            if (DislEvaluation.Holds(specification, rule, DislJson.Pointer(at, "rule"), DislContexts.Form, variables)) continue;
            return validation.TryGetProperty("message", out var message)
                ? DislEvaluation.Message(specification, message, DislJson.Pointer(at, "message"), DislContexts.Form, variables, $"'{DislEvaluation.TextOf(variables["value"])}' is not accepted here.")
                : $"'{DislEvaluation.TextOf(variables["value"])}' is not accepted here.";
        }
        return null;
    }

    /// <summary>The value of the option labelled <paramref name="text"/> in <paramref name="item"/>'s <c>options</c>, else the text itself.</summary>
    private static object? Chosen(DislSpecification specification, JsonElement item, string pointer, Dictionary<string, object?> variables, string text)
    {
        if (!item.TryGetProperty("options", out var options)) return text;
        IEnumerable<object?> list = options.ValueKind switch
        {
            JsonValueKind.Array => options.EnumerateArray().Select(DislValues.Json),
            JsonValueKind.Object when options.TryGetProperty("enum", out _) => [],
            JsonValueKind.Object or JsonValueKind.String => DislEvaluation.Expression(specification, options, DislJson.Pointer(pointer, "options"), DislContexts.Form, variables) as IReadOnlyList<object?> ?? [],
            _ => [],
        };
        foreach (var option in list)
        {
            if (option is CelMap map && map.TryGetValue("label", out var label) && label is string shown && shown == text)
            {
                return map.TryGetValue("value", out var chosen) ? chosen : text;
            }
        }
        return text;
    }

    /// <summary>The item of <paramref name="row"/>: by its <c>id</c>, else its attribute, else its literal label, sections, rows and tabs searched.</summary>
    private static (JsonElement Item, string Pointer)? Find(JsonElement owner, string pointer, string row)
    {
        if (!owner.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var at = DislJson.Pointer(pointer, index++);
            var kind = DislJson.String(item, "kind") ?? (item.TryGetProperty("attribute", out _) ? "field" : "");
            switch (kind)
            {
                case "section" or "group" or "row":
                    if (Find(item, DislJson.Pointer(at, "items"), row) is { } nested) return nested;
                    break;
                case "tabs" when item.TryGetProperty("tabs", out var tabs) && tabs.ValueKind == JsonValueKind.Array:
                    var tab = 0;
                    foreach (var each in tabs.EnumerateArray())
                    {
                        var tabAt = DislJson.Pointer(DislJson.Pointer(at, "tabs"), tab++);
                        if (Find(each, DislJson.Pointer(tabAt, "items"), row) is { } inTab) return inTab;
                    }
                    break;
                default:
                    var key = DislJson.String(item, "id") ?? DislJson.String(item, "attribute") ?? DislJson.String(item, "label");
                    if (key == row) return (item, at);
                    break;
            }
        }
        return null;
    }

    /// <summary>The form <c>for</c> the element's type whose <c>usage</c> includes <paramref name="usage"/> (the default being <c>inspector</c>), as <see cref="FormDerivation"/> finds it.</summary>
    private static (JsonElement Json, string Pointer)? FormOf(DislSpecification specification, DislElement self, string usage)
    {
        foreach (var form in DislJson.Members(specification.Root, "forms"))
        {
            var usages = DislJson.Strings(form.Value, "usage");
            if (usages.Count > 0 ? !usages.Contains(usage) : usage != "inspector") continue;
            if (DislJson.Strings(form.Value, "for").Any(self.IsA)) return (form.Value, DislJson.Pointer("/forms", form.Name));
        }
        return null;
    }

    private static Dictionary<string, object?> Variables(DislElement? self, object? value, CelMap env, DislDiagram? diagram = null) =>
        new(StringComparer.Ordinal) { ["self"] = self, ["value"] = value, ["diagram"] = diagram ?? self?.Diagram, ["env"] = env };
}
