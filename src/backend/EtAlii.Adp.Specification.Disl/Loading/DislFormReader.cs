using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads <c>forms</c> (DISL §7.5) into <see cref="DislForms"/>: each form, its items, sections, rows and tabs, every
/// expression with the program the loader compiled at its pointer, and each <c>parse</c>'s <c>write</c> as an action list.
/// </summary>
/// <remarks>
/// Reading never refuses and never reports: what the runtime threw on, skipped or read as absent when it read the
/// JSON at run time is read as a record that does the same when it is used.
/// </remarks>
internal static class DislFormReader
{
    public static DislForms Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("forms", out var forms)) return DislForms.None;
        if (DislFieldReader.NotAnObject(forms) is { } malformed) return new DislForms([], malformed);
        return new DislForms([.. forms.EnumerateObject().Select(form => Form(specification, form.Name, form.Value, JsonPointer.Append("/forms", form.Name)))], null);
    }

    private static DislForm Form(DislSpecification specification, string name, JsonElement form, string pointer)
    {
        if (DislFieldReader.NotAnObject(form) is { } malformed) return new DislForm(name, [], [], null, null, null, null, malformed);
        return new DislForm(
            name,
            JsonProperties.StringOrStrings(form, "usage"),
            JsonProperties.StringOrStrings(form, "for"),
            Items(specification, form, pointer),
            DislFieldReader.OptionalMessage(specification, form, pointer, "label"),
            JsonProperties.String(form, "icon"),
            DislFieldReader.OptionalMessage(specification, form, pointer, "submitLabel"),
            null);
    }

    /// <summary>The <c>items</c> of <paramref name="owner"/>, whose pointer is <paramref name="pointer"/>; null when it states none or they are not a list.</summary>
    private static List<DislFormItem>? Items(DislSpecification specification, JsonElement owner, string pointer)
    {
        if (!owner.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var itemsAt = JsonPointer.Append(pointer, "items");
        var list = new List<DislFormItem>();
        var index = 0;
        foreach (var item in items.EnumerateArray()) list.Add(Item(specification, item, JsonPointer.Append(itemsAt, index++)));
        return list;
    }

    private static DislFormItem Item(DislSpecification specification, JsonElement item, string pointer)
    {
        if (DislFieldReader.NotAnObject(item) is { } malformed) return new DislFormItem { NotAnObject = malformed };

        var attribute = JsonProperties.String(item, "attribute");
        var id = JsonProperties.String(item, "id");
        (IReadOnlyList<string>? marks, string? marksNotAnObject) = Marks(item);
        return new DislFormItem
        {
            Kind = JsonProperties.String(item, "kind") ?? (item.TryGetProperty("attribute", out _) ? "field" : ""),
            Attribute = attribute,
            Id = id,
            Name = id ?? attribute ?? JsonProperties.String(item, "label"),
            Visible = DislFieldReader.OptionalExpression(specification, item, pointer, "visible"),
            Label = DislFieldReader.OptionalMessage(specification, item, pointer, "label"),
            Title = DislFieldReader.OptionalMessage(specification, item, pointer, "title"),
            Items = Items(specification, item, pointer),
            Tabs = Tabs(specification, item, pointer),
            Display = DislFieldReader.OptionalExpression(specification, item, pointer, "display"),
            ValueText = JsonProperties.String(item, "value"),
            Value = item.TryGetProperty("value", out var value) && value.ValueKind != JsonValueKind.String ? DislFieldReader.Expression(specification, value, JsonPointer.Append(pointer, "value")) : null,
            Options = item.TryGetProperty("options", out var options) ? Options(specification, options, JsonPointer.Append(pointer, "options")) : null,
            OptionLabel = DislFieldReader.OptionalExpression(specification, item, pointer, "optionLabel"),
            Marks = marks,
            MarksNotAnObject = marksNotAnObject,
            Widget = JsonProperties.String(item, "widget"),
            ReadOnlyReasons = item.TryGetProperty("readOnlyReasons", out var reasons) ? DislFieldReader.Reasons(specification, reasons, JsonPointer.Append(pointer, "readOnlyReasons")) : [],
            Validations = Validations(specification, item, pointer),
            Parse = item.TryGetProperty("parse", out var parse) ? Parse(specification, parse, JsonPointer.Append(pointer, "parse")) : null,
            Initial = DislFieldReader.OptionalExpression(specification, item, pointer, "initial"),
        };
    }

    private static List<DislFormTab>? Tabs(DislSpecification specification, JsonElement item, string pointer)
    {
        if (!item.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array) return null;
        var tabsAt = JsonPointer.Append(pointer, "tabs");
        var list = new List<DislFormTab>();
        var index = 0;
        foreach (var tab in tabs.EnumerateArray())
        {
            var at = JsonPointer.Append(tabsAt, index++);
            list.Add(DislFieldReader.NotAnObject(tab) is { } malformed
                ? new DislFormTab(null, null, malformed)
                : new DislFormTab(DislFieldReader.OptionalMessage(specification, tab, at, "title"), Items(specification, tab, at), null));
        }
        return list;
    }

    /// <summary>An item's <c>options</c>, by what they are written as.</summary>
    private static DislFormOptions Options(DislSpecification specification, JsonElement options, string pointer)
    {
        var expression = DislFieldReader.Expression(specification, options, pointer);
        return options.ValueKind switch
        {
            JsonValueKind.Array => new DislFormOptions.Listed(expression, new DislActionValue.Literal(pointer, DislLiterals.Json(options))),
            JsonValueKind.Object when JsonProperties.String(options, "enum") is { } name => new DislFormOptions.Enumeration(expression, name),
            JsonValueKind.Object => new DislFormOptions.Computed(expression, options.TryGetProperty("enum", out _)),
            JsonValueKind.String => new DislFormOptions.Computed(expression, false),
            _ => new DislFormOptions.Other(expression),
        };
    }

    /// <summary>The labels of <c>widgetOptions.marks</c>, or the message reading them threw.</summary>
    private static (IReadOnlyList<string>? Marks, string? NotAnObject) Marks(JsonElement item)
    {
        if (!item.TryGetProperty("widgetOptions", out var widgetOptions)) return (null, null);
        if (DislFieldReader.NotAnObject(widgetOptions) is { } malformed) return (null, malformed);
        if (!widgetOptions.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array) return (null, null);

        var labels = new List<string>();
        foreach (var mark in marks.EnumerateArray())
        {
            if (DislFieldReader.NotAnObject(mark) is { } notAnObject) return (null, notAnObject);
            labels.Add(JsonProperties.String(mark, "label") ?? DislEvaluation.TextOf(mark.TryGetProperty("value", out var value) ? DislLiterals.Json(value) : null));
        }
        return (labels, null);
    }

    /// <summary>An item's <c>validate</c>, in order; none when it is not a list.</summary>
    private static List<DislFormValidation> Validations(DislSpecification specification, JsonElement item, string pointer)
    {
        if (!item.TryGetProperty("validate", out var validations) || validations.ValueKind != JsonValueKind.Array) return [];
        var validateAt = JsonPointer.Append(pointer, "validate");
        var list = new List<DislFormValidation>();
        var index = 0;
        foreach (var validation in validations.EnumerateArray())
        {
            var at = JsonPointer.Append(validateAt, index++);
            list.Add(DislFieldReader.NotAnObject(validation) is { } malformed
                ? new DislFormValidation("error", null, null, malformed)
                : new DislFormValidation(JsonProperties.String(validation, "severity") ?? "error", DislFieldReader.OptionalExpression(specification, validation, at, "rule"), DislFieldReader.OptionalMessage(specification, validation, at, "message"), null));
        }
        return list;
    }

    /// <summary>A <c>parse</c> (DISL 0.3 §7.5, §6.12): its <c>accepts</c>, its <c>refusal</c> and its <c>write</c>.</summary>
    internal static DislFormParse Parse(DislSpecification specification, JsonElement parse, string pointer) =>
        DislFieldReader.NotAnObject(parse) is { } malformed
            ? new DislFormParse(null, null, null, malformed)
            : new DislFormParse(
                DislFieldReader.OptionalExpression(specification, parse, pointer, "accepts"),
                DislFieldReader.OptionalMessage(specification, parse, pointer, "refusal"),
                parse.TryGetProperty("write", out var write) ? DislActionReader.List(specification, write, JsonPointer.Append(pointer, "write")) : null,
                null);
}
