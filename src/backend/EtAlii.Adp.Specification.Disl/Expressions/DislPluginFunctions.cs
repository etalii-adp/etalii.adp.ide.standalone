using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The host's implementations of the CEL functions a specification's plugins declare (DISL §13.1.1),
/// keyed by function name, handed to <see cref="DislLoader"/> when the specification is loaded.
/// </summary>
/// <remarks>
/// <para>
/// <b>A host implements the functions of a plugin it has</b>, and a function it does not implement is
/// a function of a plugin that is absent: its <c>fallback</c> is evaluated in its place, and without
/// one a call is an evaluation error, so a constraint that calls it reports that it could not be
/// evaluated rather than passing.
/// </para>
/// <para>
/// <b>An implementation receives the call's arguments as CEL values</b>: an element is the model's
/// <see cref="DislElement"/>, also where a finding's view shows it with the id it is written with. It
/// <b>MUST NOT</b> change the model or anything CEL can see (§13.1.1); one that throws a
/// <see cref="CelException"/> makes the call an evaluation error with that message.
/// </para>
/// <para>
/// <b>An implementation also receives what its declaration's <c>uses</c> names</b>, such as <c>diagram</c>, by
/// name, as the expression calling it has them; a name it has no value for makes the call an evaluation error.
/// </para>
/// </remarks>
public sealed class DislPluginFunctions
{
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, IReadOnlyDictionary<string, object?>, object?>> _implementations = new(StringComparer.Ordinal);

    /// <summary>No implementations: every plugin is absent.</summary>
    public static DislPluginFunctions None { get; } = new();

    /// <summary>Implements the plugin function <paramref name="name"/>; answers this, for chaining.</summary>
    public DislPluginFunctions Add(string name, Func<IReadOnlyList<object?>, object?> implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        return Add(name, (arguments, _) => implementation(arguments));
    }

    /// <summary>
    /// Implements the plugin function <paramref name="name"/> with an implementation that also receives what the
    /// declaration's <c>uses</c> names, by name; answers this, for chaining.
    /// </summary>
    public DislPluginFunctions Add(string name, Func<IReadOnlyList<object?>, IReadOnlyDictionary<string, object?>, object?> implementation)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(implementation);
        if (ReferenceEquals(this, None)) throw new InvalidOperationException("DislPluginFunctions.None holds no implementations; create a new instance.");
        _implementations[name] = implementation;
        return this;
    }

    internal bool TryGet(string name, out Func<IReadOnlyList<object?>, IReadOnlyDictionary<string, object?>, object?> implementation) =>
        _implementations.TryGetValue(name, out implementation!);
}
