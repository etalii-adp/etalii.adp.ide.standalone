namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// One link, as a dialog offers it: what it joins, which arrow it is, how it reads, and the
/// handle that comes back when it is chosen.
/// </summary>
/// <param name="Label">How a human reads it - `Cup of Tea -&gt; Kettle`.</param>
/// <param name="Id">
/// The link's composite key. Deliberately the key rather than the identity id: the dialog's
/// answer is looked up in the same reading of the map that produced the option, so what matters
/// is that the handle is exact, not that it survives a reload.
/// </param>
public sealed record WardleyLinkChoice(
    string Source,
    string Target,
    WardleyLinkKind Kind,
    string Label,
    string Id);
