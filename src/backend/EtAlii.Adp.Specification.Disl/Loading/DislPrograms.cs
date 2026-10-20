using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What decides a compiled expression of a specification (§12.3): its context, or none when the loader
/// does not know the position's, its source, and the names bound around it in the order they are
/// declared, joined by commas.
/// </summary>
internal readonly record struct DislProgramKey(DislContext? Context, string Source, string Bindings)
{
    public static DislProgramKey Of(DislContext? context, string source, IEnumerable<string> bindings) => new(context, source, string.Join(",", bindings));
}

/// <summary>
/// The programs of one specification, compiled once each: by the loader for every expression it walks,
/// at load for a label's text, which its inline editor reads as an Expression (<see cref="DislNotationReader"/>), and on
/// first use for an id rule the model computes (§11.5.2). The specification
/// keeps the cache, so its programs live exactly as long as it does.
/// </summary>
internal static class DislPrograms
{
    /// <summary>Every variable of any context: what a position whose context the loader does not know is compiled with.</summary>
    private static readonly string[] Everything = DislContexts.All.SelectMany(DislContexts.VariablesOf).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>A cache that compiles over <paramref name="functions"/>: the library, the plugins' functions and the user functions.</summary>
    public static CelProgramCache<DislProgramKey> Over(CelEnvironment functions) =>
        new(key => functions.Clone()
            .DeclareVariables(key.Context is { } context ? DislContexts.VariablesOf(context) : Everything)
            .DeclareVariables(key.Bindings.Length == 0 ? [] : key.Bindings.Split(','))
            .Compile(key.Source));
}
