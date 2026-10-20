using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A Message (DISL §2.3) typed at load: a string, a locale map, or a <c>{cel}</c> object; its text is
/// <see cref="Text"/>.
/// </summary>
internal abstract record DislMessage
{
    /// <summary>The text: the message's own, or <paramref name="fallback"/> when it is absent or fails.</summary>
    public abstract string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback);

    /// <summary>A string, as it is.</summary>
    public sealed record Plain(string Value) : DislMessage
    {
        public override string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback) => Value;
    }

    /// <summary>A locale map, its English or first text chosen at load; null when it has no text.</summary>
    public sealed record Localized(string? Value) : DislMessage
    {
        public override string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback) => Value ?? fallback;
    }

    /// <summary>A <c>{cel}</c> object: the text it evaluates to, or a locale map's.</summary>
    public sealed record Cel(DislExpressionField Expression) : DislMessage
    {
        public override string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback) =>
            Expression.Evaluate(specification, context, variables) switch
            {
                string text => text,
                CelMap locales => DislEvaluation.Localized(locales.Select(entry => (entry.Key, entry.Value as string))) ?? fallback,
                _ => fallback,
            };
    }

    /// <summary>Anything else: no message, so the fallback.</summary>
    public sealed record None : DislMessage
    {
        public override string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables, string fallback) => fallback;
    }
}

/// <summary>
/// A Reason (DISL §2.3) typed at load: a Message, which always applies; an object, while its <c>when</c> holds; or a
/// <c>{reason: id}</c>, while the reason <c>behavior.reasons</c> declares under that id does.
/// </summary>
internal abstract record DislReason
{
    /// <summary>The reason's text, or null when it does not apply.</summary>
    public abstract string? Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables);

    /// <summary>The first of <paramref name="reasons"/> that applies, or null.</summary>
    public static string? First(IReadOnlyList<DislReason> reasons, DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var reason in reasons)
        {
            if (reason.Text(specification, context, variables) is { } text) return text;
        }
        return null;
    }

    /// <summary>A Message, which always applies.</summary>
    public sealed record Always(DislMessage Message) : DislReason
    {
        public override string Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
            Message.Text(specification, context, variables, "");
    }

    /// <summary>An object with a <c>message</c>, which applies while its <c>when</c>, if it has one, holds.</summary>
    public sealed record While(DislExpressionField? When, DislMessage Message) : DislReason
    {
        public override string? Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
            When is not null && !When.Holds(specification, context, variables) ? null : Message.Text(specification, context, variables, "");
    }

    /// <summary>A <c>{reason: id}</c>: the named reason, declared once in the element context.</summary>
    public sealed record Named(string Id) : DislReason
    {
        public override string? Text(DislSpecification specification, DislContext context, IReadOnlyDictionary<string, object?> variables) =>
            DislEvaluation.NamedReason(specification, Id, variables);
    }
}
