using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>One row of a property inspector as a host shows it, its id mapped by a <see cref="WireIdMap"/>.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Value">The value as text.</param>
/// <param name="Widget">The item's <c>widget</c>, or null when it states none; the host chooses its editor from it.</param>
/// <param name="ReadOnlyReason">Why it cannot be edited; empty when it can.</param>
/// <param name="Group">The title of the section (or group, or tab) it is in; empty outside one.</param>
/// <param name="Candidates">What the value may be chosen from: a tags or select item's <c>options</c>, each a value or a <c>{value, label}</c> map's label, a slider's mark labels; null for none.</param>
/// <param name="Retypes">
/// Whether the row changes the element's type: an item of kind <c>type</c> (DISL 0.3 §7.5), whose
/// <c>options</c> are the types it can become and whose <c>optionLabel</c> names each.
/// </param>
public sealed record DerivedRow(string Id, string Label, string Value, string? Widget, string ReadOnlyReason, string Group, IReadOnlyList<string>? Candidates, bool Retypes = false);

/// <summary>
/// The property rows of an element (DISL §7.5): the items of the first form <c>for</c> its type whose
/// <c>usage</c> includes <c>inspector</c> (the default), sections flattened into groups, each item
/// or section left out while its <c>visible</c> fails.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fixed attribute has a row only where a form asks for it</b>, as every attribute: rows are a form's
/// items, never the type's attributes.
/// </para>
/// <para>
/// <b>A row's value</b> is its <c>display</c> when it has one (the tool's own text, DISL 0.3 §7.5), else
/// a type item's <c>optionLabel</c> for the element's own type, else a computed item's <c>value</c>,
/// else the attribute's text: a stored value as
/// CEL writes it (a <c>yearMonth</c> as <c>uuuu-MM</c>, a list joined by <c>, </c>), an unset one as
/// its default when it has one, else empty.
/// </para>
/// <para>
/// <b>A row's read-only reason</b> is the first of its <c>readOnlyReasons</c> that applies, else, for a
/// <c>fixed</c> attribute (§4.3), the first of the attribute's own, else, on a read-only diagram,
/// <c>behavior.messages</c>' <c>std.readOnly</c>, else, for a fixed attribute, the runtime's sentence.
/// The attribute's own reasons are not read for an attribute that is not fixed. A computed item without reasons
/// is the host's to accept or refuse a value for, as its <c>parse</c> says.
/// </para>
/// <para>
/// <b>A row's id</b> is the map's for the item's <c>id</c> (DISL 0.3 §7.5), else its attribute, else its label.
/// </para>
/// <para>
/// <b>A retype row</b> is an item of kind <c>type</c> (DISL 0.3 §7.5): its candidates are the types its
/// <c>options</c> give, each named by its <c>optionLabel</c> with <c>item</c> bound, and the row says
/// <see cref="DerivedRow.Retypes"/> so a host offers a choice. The candidates are <c>options</c> as they
/// evaluate, not limited further by <c>behavior.retype</c>, so the element's own type stays among them.
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
        if (specification.Forms.For(self, usage) is not { } form) return rows;

        var walk = new Walk(specification, self, env, ids, rows);
        walk.Items(form.Items, "");
        return rows;
    }

    /// <summary>
    /// Why the row of <paramref name="item"/> cannot be edited, in the order of §4.3: the first of the item's
    /// <c>readOnlyReasons</c> that applies; for a <c>fixed</c> attribute, then the first of the attribute's own; on a
    /// read-only diagram, then <c>std.readOnly</c>; and for a fixed attribute that none of them names, the runtime's
    /// sentence. Null when the row can be edited.
    /// </summary>
    internal static string? ReadOnlyReason(DislSpecification specification, DislFormItem item, DislAttribute? attribute, IReadOnlyDictionary<string, object?> variables, DislEnv env)
    {
        var fixedAttribute = attribute is { IsFixed: true };
        return DislReason.First(item.ReadOnlyReasons, specification, variables)
            ?? (fixedAttribute ? DislReason.First(attribute!.ReadOnlyReasons, specification, variables) : null)
            ?? (env.ReadOnly ? DislEvaluation.StandardMessage(specification, "std.readOnly", variables, "This diagram is read-only.") : null)
            ?? (fixedAttribute ? FixedReason : null);
    }

    /// <summary>The runtime's sentence for a <c>fixed</c> attribute (§4.3) whose field and declaration give no reason.</summary>
    private const string FixedReason = "This value is fixed by the diagram type.";

    private sealed class Walk(DislSpecification specification, DislElement self, DislEnv env, WireIdMap ids, List<DerivedRow> rows)
    {
        private readonly CelMap _env = env.ToCel();

        public void Items(IReadOnlyList<DislFormItem>? items, string group)
        {
            foreach (var item in items ?? []) Item(item, group);
        }

        private void Item(DislFormItem item, string group)
        {
            if (item.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            var attribute = item.Attribute;
            var variables = Variables(attribute is not null && self.Type.Attributes.ContainsKey(attribute) ? self.ValueOf(attribute) : null);
            if (item.Visible is { } visible && !visible.Holds(variables)) return;

            switch (item.Kind)
            {
                case "section" or "group":
                    var title = item.Label ?? item.Title;
                    Items(item.Items, title is null ? group : title.Text(variables, group));
                    return;
                case "row":
                    Items(item.Items, group);
                    return;
                case "tabs":
                    foreach (var tab in item.Tabs ?? [])
                    {
                        if (tab.NotAnObject is { } notAnObject) throw new InvalidOperationException(notAnObject);
                        Items(tab.Items, tab.Title?.Text(variables, group) ?? group);
                    }
                    return;
                case "field" or "computed" or "type":
                    rows.Add(Row(item, group, variables));
                    return;
            }
        }

        private DerivedRow Row(DislFormItem item, string group, Dictionary<string, object?> variables)
        {
            var attribute = item.Attribute;
            var declared = attribute is null ? null : self.Type.Attributes.GetValueOrDefault(attribute);
            var defaultLabel = declared is null ? attribute ?? "" : declared.Label ?? declared.Name;
            var label = item.Label?.Text(variables, defaultLabel) ?? defaultLabel;

            string value;
            if (item.Display is { } display)
            {
                value = Text(display.Evaluate(variables));
            }
            else if (item.Kind == "type")
            {
                value = OptionLabel(item, variables, self.Type.Name);
            }
            else if (item.Kind == "computed")
            {
                value = item.ValueText ?? (item.Value is { } computed ? Text(computed.Evaluate(variables)) : "");
            }
            else
            {
                value = declared is null ? "" : AttributeText(declared);
            }

            var reason = ReadOnlyReason(specification, item, declared, variables, env);

            var key = item.Id ?? attribute ?? label;
            return item.Kind == "type"
                ? new DerivedRow(ids.PropertyId(key), label, value, item.Widget, reason ?? "", group, Types(item, variables), Retypes: true)
                : new DerivedRow(ids.PropertyId(key), label, value, item.Widget, reason ?? "", group, Candidates(item, variables));
        }

        /// <summary>The types a type item offers, each named by its <c>optionLabel</c> with <c>item</c> bound, else as itself.</summary>
        private List<string> Types(DislFormItem item, Dictionary<string, object?> variables) =>
            item.Options?.Expression.Evaluate(variables) is IReadOnlyList<object?> types
                ? [.. types.Select(type => OptionLabel(item, variables, type))]
                : [];

        /// <summary>A type item's name for <paramref name="type"/>: its <c>optionLabel</c> with <c>item</c> bound, else the type as text.</summary>
        private string OptionLabel(DislFormItem item, Dictionary<string, object?> variables, object? type) =>
            item.OptionLabel is { } optionLabel
                ? Text(optionLabel.Evaluate(new Dictionary<string, object?>(variables) { ["item"] = type }))
                : Text(type);

        private List<string>? Candidates(DislFormItem item, Dictionary<string, object?> variables)
        {
            switch (item.Options)
            {
                case DislFormOptions.Listed listed:
                    return listed.Values.Value is IReadOnlyList<object?> values ? [.. values.Select(OptionText)] : [];
                case DislFormOptions.Enumeration enumeration:
                    return specification.Metamodel.Enums.TryGetValue(enumeration.Name, out var declared) ? [.. declared.Values.Select(value => value.Label ?? value.Key)] : [];
                case DislFormOptions.Computed computed:
                    return computed.Expression.Evaluate(variables) is IReadOnlyList<object?> list
                        ? [.. list.Select(OptionText)]
                        : [];
            }
            if (item.MarksNotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            return item.Marks is null ? null : [.. item.Marks];
        }

        /// <summary>An option as it is offered: a <c>{value, label}</c> map by its label, else by its value; any other value as text (§7.5).</summary>
        private static string OptionText(object? option) => option switch
        {
            CelMap map when map.TryGetValue("label", out var label) && label is string text => text,
            CelMap map when map.TryGetValue("value", out var value) => Text(value),
            _ => Text(option),
        };

        /// <summary>An attribute's text: derived or fixed, else stored, else its default when it declares one, else empty.</summary>
        private string AttributeText(DislAttribute attribute)
        {
            if (!self.Attributes.ContainsKey(attribute.Name) && attribute.Default is null && !attribute.IsDerived && !attribute.IsFixed) return "";
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
