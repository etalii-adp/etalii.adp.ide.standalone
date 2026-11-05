namespace EtAlii.Adp.Specification.Disl;

/// <summary>The forms of a specification (DISL §7.5), typed at load.</summary>
/// <param name="All">The forms, in declaration order; none when they are not an object.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime looked a form up by name in a <c>forms</c> that is not an
/// object, null for one that is; such forms still throw it on every look-up by name.
/// </param>
internal sealed record DislForms(IReadOnlyList<DislForm> All, string? NotAnObject)
{
    public static DislForms None { get; } = new([], null);

    /// <summary>The first form <c>for</c> <paramref name="self"/>'s type whose <c>usage</c> includes <paramref name="usage"/>, the default being <c>inspector</c>; null when none is.</summary>
    public DislForm? For(DislElement self, string usage) =>
        All.FirstOrDefault(form => (form.Usage.Count > 0 ? form.Usage.Contains(usage) : usage == "inspector") && form.For.Any(self.IsA));

    /// <summary>The first form <c>for</c> <c>"diagram"</c> (§7.5) whose <c>usage</c> includes <paramref name="usage"/>, the default being <c>inspector</c>; null when none is.</summary>
    public DislForm? ForDiagram(string usage) =>
        All.FirstOrDefault(form => (form.Usage.Count > 0 ? form.Usage.Contains(usage) : usage == "inspector") && form.For.Contains("diagram"));

    /// <summary>The form named <paramref name="name"/>, or null when there is none.</summary>
    public DislForm? Named(string name) =>
        NotAnObject is not null ? throw new InvalidOperationException(NotAnObject) : All.FirstOrDefault(form => form.Name == name);
}

/// <summary>A form (DISL §7.5), typed at load.</summary>
/// <param name="Items">Its items, in order; null when it states none or they are not a list.</param>
/// <param name="NotAnObject">
/// The message System.Text.Json threw when the runtime read a form that is not an object, null for one that is; such
/// a form is for nothing, and still throws it where its items were read.
/// </param>
internal sealed record DislForm(
    string Name,
    IReadOnlyList<string> Usage,
    IReadOnlyList<string> For,
    IReadOnlyList<DislFormItem>? Items,
    DislMessage? Label,
    string? Icon,
    DislMessage? SubmitLabel,
    string? NotAnObject);

/// <summary>An item of a form, a section, a row or a tab (DISL §7.5), typed at load; every member absent from the definition is null or empty.</summary>
internal sealed record DislFormItem
{
    /// <summary>Its <c>kind</c>; <c>field</c> when it states none but names an attribute, else empty.</summary>
    public string Kind { get; init; } = "";

    public string? Attribute { get; init; }

    /// <summary>Its own <c>id</c>.</summary>
    public string? Id { get; init; }

    /// <summary>The name a commit finds it by: its <c>id</c>, else its attribute, else its label when that is a string.</summary>
    public string? Name { get; init; }

    public DislExpressionField? Visible { get; init; }

    public DislMessage? Label { get; init; }

    /// <summary>A section's <c>title</c>, which it is titled by when it has no <c>label</c>.</summary>
    public DislMessage? Title { get; init; }

    /// <summary>The items of a section, group or row; null when it states none or they are not a list.</summary>
    public IReadOnlyList<DislFormItem>? Items { get; init; }

    /// <summary>The tabs of a <c>tabs</c> item; null when it states none or they are not a list.</summary>
    public IReadOnlyList<DislFormTab>? Tabs { get; init; }

    public DislExpressionField? Display { get; init; }

    /// <summary>Its <c>showAbsent</c>: whether the field is shown while its value is absent; true unless it says false.</summary>
    public bool ShowAbsent { get; init; } = true;

    /// <summary>A computed item's <c>value</c> when it is a string, which is shown as it is written.</summary>
    public string? ValueText { get; init; }

    /// <summary>A computed item's <c>value</c> when it is anything but a string: an Expression.</summary>
    public DislExpressionField? Value { get; init; }

    public DislFormOptions? Options { get; init; }

    public DislExpressionField? OptionLabel { get; init; }

    /// <summary>The labels of <c>widgetOptions.marks</c>, each its <c>label</c> or its value as text; null when there are none or they are not a list.</summary>
    public IReadOnlyList<string>? Marks { get; init; }

    /// <summary>
    /// The message System.Text.Json threw when the runtime read <c>widgetOptions</c> or a mark that is not an object,
    /// null when it threw none; the marks still throw it where they were read.
    /// </summary>
    public string? MarksNotAnObject { get; init; }

    public string? Widget { get; init; }

    public IReadOnlyList<DislReason> ReadOnlyReasons { get; init; } = [];

    /// <summary>Its <c>validate</c>, in order; none when it is not a list.</summary>
    public IReadOnlyList<DislFormValidation> Validations { get; init; } = [];

    public DislFormParse? Parse { get; init; }

    /// <summary>A params form item's <c>initial</c> value.</summary>
    public DislExpressionField? Initial { get; init; }

    /// <summary>
    /// The message System.Text.Json threw when the runtime read an item that is not an object, null for one that is;
    /// such an item still throws it where it was read.
    /// </summary>
    public string? NotAnObject { get; init; }
}

/// <summary>A tab of a <c>tabs</c> item (DISL §7.5), typed at load; one that is not an object still throws the message System.Text.Json threw where it was read.</summary>
internal sealed record DislFormTab(DislMessage? Title, IReadOnlyList<DislFormItem>? Items, string? NotAnObject);

/// <summary>
/// An item's <c>options</c> (DISL §7.5), typed at load by what it is written as. Every case is also an Expression, as
/// a type item reads it, which a value that holds no CEL fails.
/// </summary>
internal abstract record DislFormOptions(DislExpressionField Expression)
{
    /// <summary>A list: the values it offers, as written.</summary>
    public sealed record Listed(DislExpressionField Expression, DislActionValue.Literal Values) : DislFormOptions(Expression);

    /// <summary>An object whose <c>enum</c> names the enumeration it offers.</summary>
    public sealed record Enumeration(DislExpressionField Expression, string Name) : DislFormOptions(Expression);

    /// <summary>A string or an object otherwise: the list it evaluates to, though an object with an <c>enum</c> offers no value to commit.</summary>
    public sealed record Computed(DislExpressionField Expression, bool DeclaresEnum) : DislFormOptions(Expression);

    /// <summary>Anything else, which offers nothing.</summary>
    public sealed record Other(DislExpressionField Expression) : DislFormOptions(Expression);
}

/// <summary>One <c>validate</c> of an item (DISL §7.5), typed at load; one that is not an object still throws the message System.Text.Json threw where its <c>rule</c> was read.</summary>
/// <param name="Severity">Its <c>severity</c>, <c>error</c> when it states none.</param>
/// <param name="Rule">Its <c>rule</c>, or null when it has none and judges nothing.</param>
internal sealed record DislFormValidation(string Severity, DislExpressionField? Rule, DislMessage? Message, string? NotAnObject);

/// <summary>
/// An item's <c>parse</c> (DISL 0.3 §7.5), typed at load: its <c>accepts</c>, its <c>refusal</c>, and its <c>write</c>,
/// null when it has none; one that is not an object still throws the message System.Text.Json threw where it was read.
/// </summary>
internal sealed record DislFormParse(DislExpressionField? Accepts, DislMessage? Refusal, DislActionList? Write, string? NotAnObject);
