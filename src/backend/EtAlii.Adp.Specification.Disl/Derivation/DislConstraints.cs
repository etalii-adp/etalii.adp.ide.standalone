namespace EtAlii.Adp.Specification.Disl;

/// <summary>The <c>constraints</c> of a specification (DISL §8), typed at load: its rules, its defaults, its order and the built-ins' settings.</summary>
/// <param name="Rules">The rules, in declaration order; null when <c>constraints</c> is not an object or its <c>rules</c> not a list.</param>
/// <param name="DefaultTiming">The <c>defaults.timing</c> a rule without its own takes; none when it states none.</param>
/// <param name="DefaultSeverity">The <c>defaults.severity</c> a rule without its own takes; null when it states none.</param>
/// <param name="Order">The codes of <c>order</c> (DISL 0.3 §8.6), in order; none when it states none.</param>
/// <param name="BuiltIns">The settings of <c>builtIn</c> (§8.1) that are objects, by built-in id.</param>
internal sealed record DislConstraints(
    IReadOnlyList<DislConstraintRule>? Rules,
    IReadOnlyList<string> DefaultTiming,
    string? DefaultSeverity,
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, DislBuiltIn> BuiltIns)
{
    public static DislConstraints None { get; } = new(null, [], null, [], new Dictionary<string, DislBuiltIn>());

    /// <summary>The setting of the built-in <paramref name="id"/>: its own, or the runtime's defaults when it states none or not as an object.</summary>
    public DislBuiltIn BuiltIn(string id) => BuiltIns.GetValueOrDefault(id) ?? DislBuiltIn.Default;
}

/// <summary>A rule of <c>constraints.rules</c> (DISL §8.2 to §8.4), typed at load; every member absent from the definition is null or empty.</summary>
internal sealed record DislConstraintRule
{
    /// <summary>Where the rule is, which its id is when it states none.</summary>
    public string Pointer { get; init; } = "";

    /// <summary>Its <c>id</c>, else its pointer.</summary>
    public string Id { get; init; } = "";

    /// <summary>Its <c>code</c>, else its id.</summary>
    public string Code { get; init; } = "";

    /// <summary>Its <c>kind</c>: null or <c>invariant</c> for an invariant, else the gesture it refuses (§8.4).</summary>
    public string? Kind { get; init; }

    /// <summary>Whether it is evaluated: unless its <c>enabled</c> is <c>false</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Its <c>enforcement</c>, or null when it states none.</summary>
    public string? Enforcement { get; init; }

    /// <summary>Its own <c>timing</c>; none when it states none.</summary>
    public IReadOnlyList<string> Timing { get; init; } = [];

    /// <summary>Its <c>scope</c>: types, or <c>diagram</c>, <c>node</c>, <c>relation</c> or <c>*</c>; none when it states none.</summary>
    public IReadOnlyList<string> Scope { get; init; } = [];

    public DislExpressionField? ForEach { get; init; }

    public DislExpressionField? When { get; init; }

    /// <summary>Its <c>rule</c>, the condition that must hold.</summary>
    public DislExpressionField? Rule { get; init; }

    /// <summary>Its <c>severity</c> when it is a string.</summary>
    public string? SeverityText { get; init; }

    /// <summary>Its <c>severity</c> when it is anything but a string: an Expression, whose value is the severity when it is a string.</summary>
    public DislExpressionField? Severity { get; init; }

    public DislMessage? Message { get; init; }

    public DislExpressionField? Location { get; init; }

    public DislExpressionField? Subject { get; init; }

    /// <summary>Its <c>target</c>, or null when it states none or states <c>self</c>.</summary>
    public DislExpressionField? Target { get; init; }

    /// <summary>
    /// The message System.Text.Json threw when the runtime read an invariant that is not an object, null for one that is;
    /// such a rule still throws it where its <c>enabled</c> was read.
    /// </summary>
    public string? NotAnObject { get; init; }
}

/// <summary>The setting of one built-in in <c>constraints.builtIn</c> (DISL §8.1), typed at load.</summary>
/// <param name="Enabled">Whether it is reported: unless its <c>enabled</c> is <c>false</c>.</param>
/// <param name="Severity">Its <c>severity</c>, or null for the runtime's.</param>
/// <param name="OncePerGroup">Its <c>oncePerGroup</c> (DISL 0.3 §8.1), or null when it states none.</param>
/// <param name="CodeText">Its <c>code</c> when it is a string.</param>
/// <param name="Code">Its <c>code</c> when it is an object: a <c>{cel}</c> computed per finding.</param>
/// <param name="Message">Its <c>message</c>, or null when it states none.</param>
/// <param name="Refusal">Its <c>refusal</c> (§8.1, §8.4), the wording of a gesture it refuses, or null when it states none.</param>
internal sealed record DislBuiltIn(bool Enabled, string? Severity, string? OncePerGroup, string? CodeText, DislExpressionField? Code, DislMessage? Message, DislMessage? Refusal = null)
{
    /// <summary>A built-in the definition configures nothing of.</summary>
    public static DislBuiltIn Default { get; } = new(true, null, null, null, null, null);
}
