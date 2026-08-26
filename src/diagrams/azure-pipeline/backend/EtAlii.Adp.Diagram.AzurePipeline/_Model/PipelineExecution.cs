namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The properties that decide whether and how an element runs (Requirement 4.5).
/// </summary>
/// <remarks>
/// <para>
/// One record shared by stages, jobs and steps, because Azure spells these the same way at every
/// level and a reader asking "will this run?" is asking the same question at every level. A level
/// that has no such key simply leaves it empty.
/// </para>
/// <para>
/// Every value is a string, not a bool or an int, because every one of them may be an expression:
/// <c>condition: ${{ if ... }}</c>, <c>continueOnError: $[ variables.soft ]</c>,
/// <c>timeoutInMinutes: $(timeout)</c>. Parsing them into typed values would mean either evaluating
/// an expression this module refuses to evaluate, or losing it.
/// </para>
/// <para>
/// <see cref="DeclaredKeys"/> is what separates <b>absent</b> from <b>empty</b>. A stage with no
/// <c>condition</c> and a stage with <c>condition: ''</c> both leave the string empty, but they are
/// different states of the file and Requirement 13.10 says the panel must show them differently -
/// one has no row, the other a row whose value is empty. Carrying the set costs less than a
/// sentinel value would, and cannot be mistaken for a real value the way a sentinel eventually is.
/// </para>
/// </remarks>
/// <param name="Condition">Its <c>condition</c>, verbatim.</param>
/// <param name="ContinueOnError">Its <c>continueOnError</c>, verbatim.</param>
/// <param name="Enabled">Its <c>enabled</c>, verbatim. Absent means enabled.</param>
/// <param name="TimeoutInMinutes">Its <c>timeoutInMinutes</c>, verbatim.</param>
/// <param name="DeclaredKeys">Which of these the document actually carries, whatever their values.</param>
public sealed record PipelineExecution(
    string Condition,
    string ContinueOnError,
    string Enabled,
    string TimeoutInMinutes,
    IReadOnlySet<string> DeclaredKeys)
{
    /// <summary>The <c>condition</c> key, as the file spells it.</summary>
    public const string ConditionKey = "condition";

    /// <summary>The <c>continueOnError</c> key.</summary>
    public const string ContinueOnErrorKey = "continueOnError";

    /// <summary>The <c>enabled</c> key.</summary>
    public const string EnabledKey = "enabled";

    /// <summary>The <c>timeoutInMinutes</c> key.</summary>
    public const string TimeoutKey = "timeoutInMinutes";

    /// <summary>An element that declares none of these, and so runs on the defaults.</summary>
    public static PipelineExecution Default { get; } =
        new("", "", "", "", new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Whether the document carries <paramref name="key"/> at all, whatever its value.</summary>
    public bool Has(string key) => DeclaredKeys.Contains(key);

    /// <summary>Whether a <c>condition</c> was declared, which is what the canvas marks.</summary>
    public bool HasCondition => Has(ConditionKey);

    /// <summary>
    /// Whether the element is switched off outright. Only a literal <c>false</c> counts: an
    /// expression is not knowable here, and guessing would draw a step as skipped that runs.
    /// </summary>
    public bool IsDisabled => string.Equals(Enabled, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a failure is declared not to fail the run. Only a literal <c>true</c> counts.</summary>
    public bool ContinuesOnError => string.Equals(ContinueOnError, "true", StringComparison.OrdinalIgnoreCase);
}
