namespace EtAlii.Adp.Specification.Disl;

/// <summary>How bad a <see cref="DislDiagnostic"/> is (DISL §14.1): an error makes the specification unusable, a warning does not.</summary>
public enum DislSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One thing loading a specification found wrong with it (DISL §14.1): where, as the JSON Pointer of
/// the offending value, how bad, and what, in a sentence a tool engineer can act on.
/// </summary>
public sealed record DislDiagnostic(string Pointer, DislSeverity Severity, string Message)
{
    public override string ToString() => $"{(Severity == DislSeverity.Error ? "error" : "warning")} at {(Pointer.Length == 0 ? "/" : Pointer)}: {Message}";
}
