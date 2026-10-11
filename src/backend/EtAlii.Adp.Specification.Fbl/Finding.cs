namespace EtAlii.Adp.Specification.Fbl;

public enum FindingSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A problem found while reading a body or a registration (FBL §7.4). Reading never fails on content:
/// whatever it cannot read becomes one of these.
/// </summary>
public sealed record Finding(string Code, FindingSeverity Severity, string Message, SourceLocation? Location)
{
    public override string ToString() => Location is null ? $"{Code}: {Message}" : $"{Code} at {Location}: {Message}";
}

/// <summary>DISL's source location: the file relative to the subject, and the line, column and length of the entry's own span.</summary>
public sealed record SourceLocation(string File, int Line, int Column, int Length)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>The finding codes FBL defines (FBL §7.4) and the DISL ones it uses (DISL §8.6).</summary>
public static class FindingCodes
{
    public const string UnboundStatement = "fbl.unbound-statement";
    public const string UnboundKey = "fbl.unbound-key";
    public const string DanglingReference = "fbl.dangling-reference";
    public const string HeaderMismatch = "fbl.header-mismatch";
    public const string DuplicateKey = "fbl.duplicate-key";
    public const string MissingBody = "fbl.missing-body";
    public const string StaleViewData = "fbl.stale-view-data";
    public const string UnknownHeader = "fbl.unknown-header";
    public const string RegexTimeout = "fbl.regex-timeout";
    public const string Unparseable = "std.unparseable";
    public const string UnreadableEntry = "std.unreadableEntry";
    public const string MissingId = "std.missingId";
    public const string DuplicateId = "std.duplicateId";
    public const string PluginMissing = "std.pluginMissing";
}
