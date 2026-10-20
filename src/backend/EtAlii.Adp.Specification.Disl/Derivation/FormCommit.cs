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

        if (specification.Forms.For(self, usage) is not { } form || Find(form.Items, row) is not { } item)
        {
            return DislTransaction.Refused($"'{row}' is not a row of this element's form.");
        }

        var declared = item.Attribute is null ? null : self.Type.Attributes.GetValueOrDefault(item.Attribute);
        var cel = env.ToCel();
        var current = Variables(self, declared is null ? null : self.ValueOf(declared.Name), cel);

        if (DislReason.First(item.ReadOnlyReasons, specification, current) is { } reason) return DislTransaction.Refused(reason);
        if (env.ReadOnly) return DislTransaction.Refused(DislEvaluation.StandardMessage(specification, "std.readOnly", current, "This diagram is read-only."));

        var chosen = Chosen(specification, item, current, value);
        if (Invalid(specification, item, Variables(self, chosen, cel)) is { } invalid) return DislTransaction.Refused(invalid);

        if (item.Parse is { } parse)
        {
            if (parse.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            var text = Variables(self, value, cel);
            if (parse.Accepts is { } accepts && !accepts.Holds(text))
            {
                return DislTransaction.Refused(parse.Refusal?.Text(text, $"'{value}' cannot be written here.") ?? $"'{value}' cannot be written here.");
            }

            var runner = new ActionRunner(specification, self.Diagram, DislIds.Fixed());
            if (parse.Write is { } write) runner.Run(write, text);
            return runner.Transaction;
        }

        if (declared is null || item.Kind == "computed")
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
    /// form on the diagram, whose <paramref name="diagram"/> is then given): its label, icon and <c>submitLabel</c>, and
    /// each item's label, widget and <c>initial</c> value, evaluated once; null when there is no such form.
    /// </summary>
    public static FormRequest? Request(DislSpecification specification, string form, DislElement? self, DislEnv env, DislDiagram? diagram = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(env);

        if (specification.Forms.Named(form) is not { } found) return null;
        if (found.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        var variables = Variables(self, null, env.ToCel(), diagram ?? self?.Diagram);

        var fields = new List<FormRequestField>();
        foreach (var item in found.Items ?? [])
        {
            var attribute = item.Attribute ?? "";
            if (item.NotAnObject is { } notAnObject) throw new InvalidOperationException(notAnObject);
            var label = item.Label?.Text(variables, attribute) ?? attribute;
            var initial = item.Initial?.Evaluate(variables);
            fields.Add(new FormRequestField(attribute, label, item.Widget, initial is CelError ? null : initial));
        }

        return new FormRequest(
            found.Label?.Text(variables, form) ?? form,
            found.Icon,
            found.SubmitLabel?.Text(variables, ""),
            fields);
    }

    /// <summary>
    /// Why the params form <paramref name="form"/> refuses <paramref name="value"/> for its item
    /// <paramref name="attribute"/>: the message of the first of its validations of severity <c>error</c> that fails;
    /// null when the value is accepted. For a form on the diagram, <paramref name="diagram"/> is given.
    /// </summary>
    public static string? Refusal(DislSpecification specification, string form, string attribute, object? value, DislElement? self, DislEnv env, DislDiagram? diagram = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(env);

        if (specification.Forms.Named(form) is not { } found) return null;
        if (found.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);

        foreach (var item in found.Items ?? [])
        {
            if (item.Attribute != attribute) continue;
            return Invalid(specification, item, Variables(self, value, env.ToCel(), diagram ?? self?.Diagram));
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
        if (specification.Notation.LabelOf(self.Type.Name, label) is not { Parse: { } parse })
        {
            return DislTransaction.Refused($"'{label}' cannot be edited here.");
        }

        // A parse that is not an object threw here when it was read from the JSON at run time, and still does.
        if (parse.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (parse.Accepts is { } accepts && !accepts.Holds(variables))
        {
            return DislTransaction.Refused(parse.Refusal?.Text(variables, $"'{text}' cannot be written here.") ?? $"'{text}' cannot be written here.");
        }

        var runner = new ActionRunner(specification, self.Diagram, DislIds.Fixed());
        if (parse.Write is { } write) runner.Run(write, variables);
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

        return specification.Notation.LabelOf(self.Type.Name, label)?.EditText?.Evaluate(Variables(self, null, env.ToCel())) as string;
    }

    /// <summary>The message of the first validation of severity <c>error</c> of <paramref name="item"/> that fails, or null.</summary>
    private static string? Invalid(DislSpecification specification, DislFormItem item, Dictionary<string, object?> variables)
    {
        if (item.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        foreach (var validation in item.Validations)
        {
            if (validation.NotAnObject is { } notAnObject) throw new InvalidOperationException(notAnObject);
            if (validation.Severity != "error" || validation.Rule is not { } rule) continue;
            if (rule.Holds(variables)) continue;
            var fallback = $"'{DislEvaluation.TextOf(variables["value"])}' is not accepted here.";
            return validation.Message?.Text(variables, fallback) ?? fallback;
        }
        return null;
    }

    /// <summary>The value of the option labelled <paramref name="text"/> in <paramref name="item"/>'s <c>options</c>, else the text itself.</summary>
    private static object? Chosen(DislSpecification specification, DislFormItem item, Dictionary<string, object?> variables, string text)
    {
        if (item.Options is not { } options) return text;
        IEnumerable<object?> list = options switch
        {
            DislFormOptions.Listed listed => listed.Values.Evaluate(variables) as IReadOnlyList<object?> ?? [],
            DislFormOptions.Computed { DeclaresEnum: false } computed => computed.Expression.Evaluate(variables) as IReadOnlyList<object?> ?? [],
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
    private static DislFormItem? Find(IReadOnlyList<DislFormItem>? items, string row)
    {
        foreach (var item in items ?? [])
        {
            if (item.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            switch (item.Kind)
            {
                case "section" or "group" or "row":
                    if (Find(item.Items, row) is { } nested) return nested;
                    break;
                case "tabs" when item.Tabs is { } tabs:
                    foreach (var tab in tabs)
                    {
                        if (tab.NotAnObject is { } notAnObject) throw new InvalidOperationException(notAnObject);
                        if (Find(tab.Items, row) is { } inTab) return inTab;
                    }
                    break;
                default:
                    if (item.Name == row) return item;
                    break;
            }
        }
        return null;
    }

    private static Dictionary<string, object?> Variables(DislElement? self, object? value, CelMap env, DislDiagram? diagram = null) =>
        new(StringComparer.Ordinal) { ["self"] = self, ["value"] = value, ["diagram"] = diagram ?? self?.Diagram, ["env"] = env };
}
