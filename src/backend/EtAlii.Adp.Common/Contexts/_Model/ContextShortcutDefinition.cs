namespace EtAlii.Adp.Common;

/// <summary>
/// A keypress an action answers to, expressed exactly as the client will see it on a
/// <c>KeyboardEvent</c>, so no client-side key-to-action table is needed.
/// </summary>
public sealed record ContextShortcutDefinition(
    string Key,
    bool Ctrl = false,
    bool Shift = false,
    bool Alt = false,
    bool Meta = false)
{
    public bool Matches(ContextShortcutDefinition other) =>
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase) &&
        Ctrl == other.Ctrl &&
        Shift == other.Shift &&
        Alt == other.Alt &&
        Meta == other.Meta;
}
