namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// One finding (DISL §8.6) as the runtime gives it to a host, which maps it onto its own type: the
/// code, the rule that raised it, the severity, the message, the elements it is about and the line.
/// </summary>
/// <param name="Code">The rule's <c>code</c>, else its id.</param>
/// <param name="ConstraintId">The id of the declared rule, or of the built-in (<c>std.endpoints</c>).</param>
/// <param name="Severity"><c>error</c>, <c>warning</c>, <c>info</c> or <c>hint</c>.</param>
/// <param name="Message">The rule's Message, evaluated.</param>
/// <param name="ElementIds">The ids of what the finding is about, as the host writes them.</param>
/// <param name="Line">The 1-based line it is located at, when it has one.</param>
public sealed record DislFinding(string Code, string ConstraintId, string Severity, string Message, IReadOnlyList<string> ElementIds, int? Line);

/// <summary>
/// A finding the reader raised (DISL §8.7): <c>std.unparseable</c> or <c>std.unreadableEntry</c>,
/// with the <c>detail</c> its message is evaluated with and its line.
/// </summary>
/// <param name="BuiltIn">The built-in it is reported as.</param>
/// <param name="Detail">Bound as <c>detail</c> in the built-in's message: at least <c>reason</c>.</param>
/// <param name="Line">The 1-based line, when the reader knows it.</param>
public sealed record DislReaderFinding(string BuiltIn, IReadOnlyDictionary<string, object?> Detail, int? Line)
{
    public const string Unparseable = "std.unparseable";
    private const string UnreadableEntry = "std.unreadableEntry";

    /// <summary>An entry the reader could not make an element of, for <paramref name="reason"/>.</summary>
    public static DislReaderFinding Unreadable(string reason, int? line) => new(UnreadableEntry, new Dictionary<string, object?> { ["reason"] = reason }, line);

    /// <summary>A file the reader could not parse, for <paramref name="reason"/>.</summary>
    public static DislReaderFinding NotParsed(string reason, int? line) => new(Unparseable, new Dictionary<string, object?> { ["reason"] = reason }, line);
}

/// <summary>What the constraint evaluator is given beside the specification and the diagram.</summary>
/// <param name="Env">What <c>env</c> reads.</param>
/// <param name="ReaderFindings">The reader's findings, in reading order.</param>
/// <param name="WrittenId">
/// The id an element is written with, which a finding names and <c>self.id</c> reads in its message;
/// null for <see cref="DislElement.Id"/>. A later holder of a written id has an ephemeral id in the
/// model (§11.5.4), while its file still names it by what is written.
/// </param>
public sealed record DislConstraintOptions(DislEnv? Env = null, IReadOnlyList<DislReaderFinding>? ReaderFindings = null, Func<DislElement, string>? WrittenId = null);
