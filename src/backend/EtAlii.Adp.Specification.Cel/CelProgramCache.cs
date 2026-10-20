using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// Compiled programs by what identifies them, each compiled once and kept for as long as the cache
/// lives: the one place a language keeps the programs it compiles (FBL and DISL alike).
/// </summary>
/// <remarks>
/// <para>
/// <b>The key is what decides the program</b>: the expression and whatever decides the environment it
/// is compiled in, a context, the names bound around it. The compiler given to the cache makes the
/// program of a key and nothing else, so two equal keys always stand for one program.
/// </para>
/// <para>
/// <b>Safe under concurrent use.</b> A key is compiled by one caller while the others asking for it
/// wait, and every caller is given the same program.
/// </para>
/// <para>
/// <b>An expression that does not compile is kept as its reason</b>, the message of the
/// <see cref="CelException"/> compiling it threw, so it is not compiled again either.
/// </para>
/// <para>
/// <b>The cache lives as long as its owner</b>, and so do its programs: a specification keeps the
/// cache of its expressions, a reading of a body the cache of what it compiled while reading.
/// </para>
/// </remarks>
/// <typeparam name="TKey">What identifies a program.</typeparam>
public sealed class CelProgramCache<TKey> where TKey : notnull
{
    private readonly Func<TKey, CelProgram> _compile;
    private readonly ConcurrentDictionary<TKey, Lazy<Compilation>> _compilations = new();

    /// <param name="compile">Compiles the program of a key; a <see cref="CelException"/> says why it cannot.</param>
    public CelProgramCache(Func<TKey, CelProgram> compile)
    {
        ArgumentNullException.ThrowIfNull(compile);
        _compile = compile;
    }

    /// <summary>
    /// The program of <paramref name="key"/>, compiled on first use; false when it does not compile, with
    /// <paramref name="problem"/> the reason.
    /// </summary>
    public bool TryGet(TKey key, [NotNullWhen(true)] out CelProgram? program, [NotNullWhen(false)] out string? problem)
    {
        // The Lazy makes one compilation of a key: GetOrAdd may make a Lazy per racing caller, but it
        // stores one and gives that one to all of them, and only a stored Lazy is ever asked its value.
        var compilation = _compilations.GetOrAdd(key, static (missing, cache) => new Lazy<Compilation>(() => cache.Compile(missing), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
        program = compilation.Program;
        problem = compilation.Problem;
        return program is not null;
    }

    private Compilation Compile(TKey key)
    {
        try
        {
            return new Compilation(_compile(key), null);
        }
        catch (CelException e)
        {
            return new Compilation(null, e.Message);
        }
    }

    /// <summary>A key's outcome: its program, or the reason it has none.</summary>
    private sealed record Compilation(CelProgram? Program, string? Problem);
}
