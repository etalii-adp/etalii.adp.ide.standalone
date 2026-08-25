namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// One editable - or merely visible - property of whatever is selected.
/// </summary>
/// <remarks>
/// Described as data, like a context action and a toolbox entry, so the property grid renders a
/// row it does not understand. Nothing in the shell knows what a technology or a condition is;
/// it knows there is a property with a label, a value and an editor.
/// <para>
/// <see cref="ContextPropertyEditor"/> is the enum generated from <c>context.proto</c>, used
/// directly the way <c>ContextScope</c> already is - one name for the wire and for the domain,
/// so there is nothing to map and nothing that can drift. Widening it is a backward-compatible
/// proto change, which is what makes it safe to start with the three editors that have a
/// consumer today.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable within its provider, and what <see cref="IContextPropertyProvider.SetAsync"/> is given
/// back. Prefixed by convention like an action id - <c>c4.description</c> - so a problem in a log
/// names the module that owns it.
/// </param>
/// <param name="Label">What the row is called. The provider's words, not the shell's.</param>
/// <param name="Value">
/// The current value, as text. A <see cref="ContextPropertyEditor.Toggle"/> carries
/// <c>"true"</c> or <c>"false"</c>; everything else carries itself. A property that is absent
/// rather than empty is not contributed at all, which keeps the two distinguishable without a
/// sentinel.
/// </param>
/// <param name="Editor">Which control the grid should draw.</param>
/// <param name="ReadOnlyReason">
/// Empty when the property can be edited. Otherwise a sentence saying why it cannot be, which
/// the grid shows beside the value - a property whose source is elsewhere is worth *showing*,
/// and a reader who cannot change it deserves to know what would have to change instead. This
/// mirrors how an unavailable action carries a reason rather than being omitted: silence would
/// leave the reader wondering whether the tool was broken.
/// </param>
/// <param name="Group">
/// An optional heading the grid sorts under, for a selection with more properties than reads
/// comfortably as one list. Empty means ungrouped, and ungrouped rows come first.
/// </param>
public sealed record ContextPropertyDefinition(
    string Id,
    string Label,
    string Value,
    ContextPropertyEditor Editor = ContextPropertyEditor.Line,
    string ReadOnlyReason = "",
    string Group = "")
{
    /// <summary>Whether the grid should let the value be edited.</summary>
    public bool IsEditable => ReadOnlyReason.Length == 0;
}
