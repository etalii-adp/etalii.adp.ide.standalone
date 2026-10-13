namespace EtAlii.Adp.Editor;

/// <summary>How serious a <see cref="TextFinding"/> is.</summary>
public enum TextFindingSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One thing an analyser found in a text: a range of UTF-16 offsets into the text it was given,
/// a severity, the sentence shown to the user, and what found it.
/// </summary>
public sealed record TextFinding(int From, int To, TextFindingSeverity Severity, string Message, string Source);

/// <summary>
/// An analyser's answer for one text: its findings, its status line, and the full paths of the
/// project files the answer was computed from, which the client watches so that a change to
/// any of them asks again.
/// </summary>
public sealed record TextAnalysis(IReadOnlyList<TextFinding> Findings, string Status, IReadOnlyList<string> DependsOn)
{
    public static TextAnalysis Empty { get; } = new([], "", []);
}
