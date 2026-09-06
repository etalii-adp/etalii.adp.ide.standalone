namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The characters no entry name may contain - the same set on every platform, deliberately.
/// A project is checked out on Windows and Linux alike, so a name Linux would happily create
/// (<c>report?.txt</c>, <c>a|b</c>) is a name Windows can never check out; judging by the
/// local platform's <c>Path.GetInvalidFileNameChars()</c> made validation permissive exactly
/// where the files travel. This is the Windows superset - the strictest common denominator -
/// which is also what <see cref="EntryNameRules"/>' rejection message always promised.
/// </summary>
public static class PortableFileNames
{
    public static readonly char[] InvalidChars =
        [.. "\\/:*?\"<>|", .. Enumerable.Range(0, 32).Select(value => (char)value)];
}
