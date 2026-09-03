namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The one function that names every node (skos-diagram Requirement 3). Deterministic all the
/// way down: the approved preference order over languages, and within a tag the lexicographically
/// smallest literal - so a file that violates SKOS S14 with duplicate preferred labels still
/// renders the same on every open while the validator reports it.
/// </summary>
public static class SkosLabels
{
    /// <summary>The language that heads the default preference order.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>
    /// The name to display, chosen from <paramref name="labels"/> under
    /// <paramref name="displayLanguage"/> (lowercased; the registration's <c>language:</c> header
    /// or <see cref="DefaultLanguage"/>). Preference: the display language, then
    /// <see cref="DefaultLanguage"/>, then the untagged literal, then the lexicographically
    /// smallest remaining tag. No preferred label in any language falls back to an alternate
    /// label by the same order, marked; no label at all falls back to
    /// <paramref name="iriDisplay"/>, dimmed - and validation reports it. Hidden labels never
    /// display.
    /// </summary>
    public static SkosChosenLabel Choose(IReadOnlyList<SkosLabel> labels, string displayLanguage, string iriDisplay)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLanguage);
        ArgumentNullException.ThrowIfNull(iriDisplay);

        var language = displayLanguage.ToLowerInvariant();
        return Pick(labels, SkosLabelSource.Preferred, language, SkosLabelKind.Preferred)
            ?? Pick(labels, SkosLabelSource.Alternate, language, SkosLabelKind.Alternate)
            ?? new SkosChosenLabel(iriDisplay, "", SkosLabelKind.IriFallback);
    }

    private static SkosChosenLabel? Pick(IReadOnlyList<SkosLabel> labels, SkosLabelSource source, string language, SkosLabelKind kind)
    {
        var candidates = labels.Where(label => label.Source == source).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return In(candidates.Where(label => label.Language == language), language, kind)
            ?? In(candidates.Where(label => label.Language == DefaultLanguage), DefaultLanguage, kind)
            ?? In(candidates.Where(label => label.Language is null), "", kind)
            ?? Smallest(candidates, kind);
    }

    private static SkosChosenLabel? In(IEnumerable<SkosLabel> labels, string tag, SkosLabelKind kind)
    {
        // Within one tag, the lexicographically smallest literal - the deliberate,
        // arbitrary-but-stable S14 tie-break.
        var text = labels.Select(label => label.Text).Order(StringComparer.Ordinal).FirstOrDefault();
        return text is null ? null : new SkosChosenLabel(text, tag, kind);
    }

    private static SkosChosenLabel? Smallest(IReadOnlyList<SkosLabel> candidates, SkosLabelKind kind)
    {
        var tag = candidates
            .Select(label => label.Language)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        return tag is null ? null : In(candidates.Where(label => label.Language == tag), tag, kind);
    }
}
