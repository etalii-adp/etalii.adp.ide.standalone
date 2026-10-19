namespace EtAlii.Adp.Specification.Json;

/// <summary>How bad a <see cref="LoadDiagnostic"/> is (DISL §14.1, FBL §14.1): an error makes the document unusable, a warning does not.</summary>
public enum LoadSeverity
{
    Warning,
    Error,
}

/// <summary>
/// One thing loading a DISL specification or an FBL document found wrong with it (DISL §14.1, FBL §14.1):
/// where, as the JSON Pointer of the offending value, how bad, and what, in a sentence a tool engineer can act on.
/// </summary>
public sealed record LoadDiagnostic(string Pointer, LoadSeverity Severity, string Message)
{
    public override string ToString() => $"{(Severity == LoadSeverity.Error ? "error" : "warning")} at {(Pointer.Length == 0 ? "/" : Pointer)}: {Message}";
}
