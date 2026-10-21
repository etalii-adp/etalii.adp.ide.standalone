using System.Globalization;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The steps a fixture needs beyond the common edit sequence to show the finding it was written
/// for, each run on a fresh copy after the common sequence has been undone.
/// </summary>
/// <remarks>
/// <para>
/// <b>One entry per fixture that needs one, named by the finding of
/// <c>functional-decomposition-graph-disl-fbl</c> it shows.</b> A fixture whose finding the common
/// sequence already reaches - a trailing comment lost by a rename, a key placed after the first
/// line, a comment kept above a removed element, a refused connect into a missing section, a new
/// entry's indentation - has no entry here.
/// </para>
/// <para>
/// <b>A probe reads back what it wrote</b>, because a value written plain and read back as a
/// collection or an alias is visible only in the reading, not in the hunk.
/// </para>
/// </remarks>
internal static class FdgTranscriptProbes
{
    /// <summary>A culture whose decimal separator is a comma, made rather than looked up so it exists on every machine.</summary>
    private static readonly CultureInfo _comma = CommaCulture();

    private static readonly Dictionary<string, Func<FdgTranscript.Script, Task>> _probes = new(StringComparer.Ordinal)
    {
        // A15: the two sentences that interpolate a number, under a comma culture.
        ["width-fraction.fdg"] = async script => await script.FindingsUnderAsync("a culture whose decimal separator is a comma", _comma),

        // A11 and B3: an element of an unknown type has a menu and rows, and is renamed and removed like any other.
        ["unknown-types.fdg"] = async script =>
        {
            await script.SetAsync("odd", FdgContextPropertyProvider.NameProperty, "Renamed screen");
            await script.ReadBackAsync("odd");
            if (await script.ExecuteAsync("odd", FdgContextActionProvider.RemoveActionId) is "confirm")
            {
                await script.CommitAsync("odd", FdgContextActionProvider.RemoveActionId, "");
            }

            await script.ReadBackAsync("u1");
        },

        // B12: names the writer writes plain, each read back after it is written.
        ["names-to-quote.fdg"] = async script =>
        {
            await script.SetAsync("n1", FdgContextPropertyProvider.NameProperty, "[draft]");
            await script.ReadBackAsync("n1");
            await script.SetAsync("n2", FdgContextPropertyProvider.NameProperty, "{a}");
            await script.ReadBackAsync("n2");
            await script.SetAsync("n3", FdgContextPropertyProvider.NameProperty, "*x");
            await script.ReadBackAsync("n3");
        },

        // B13: a Comment's text, a one-line one kept on one line, one beginning with a space, one indented.
        ["comment-text-forms.fdg"] = async script =>
        {
            await script.SetAsync("t1", FdgContextPropertyProvider.TextProperty, "Still one line");
            await script.ReadBackAsync("t1");
            await script.SetAsync("t2", FdgContextPropertyProvider.TextProperty, " leading space");
            await script.ReadBackAsync("t2");
            await script.SetAsync("t3", FdgContextPropertyProvider.TextProperty, "  indented\nsecond");
            await script.ReadBackAsync("t3");
        },

        // B14: the first add into an empty flow list that carries a comment, and what the text then reads.
        ["empty-list-comment.fdg"] = async script =>
        {
            await script.AddAsync(FdgElementTypes.UiElement, "first", 240, 160);
            await script.ReadBackAsync("first");
        },

        // B19: four decimals, a half, a value rounding to zero from below, and blank descriptions.
        ["number-rounding.fdg"] = async script =>
        {
            await script.ReadBackAsync("r1");
            await script.SetAsync("r1", FdgContextPropertyProvider.WidthProperty, "200.00005");
            await script.SetAsync("r1", FdgContextPropertyProvider.WidthProperty, "100.55555");
            await script.SetAsync("r1", FdgContextPropertyProvider.WidthProperty, "100.00015");
            await script.MoveAsync("r1", -0.00004, 0.00005);
            await script.ReadBackAsync("r1");
            await script.SetAsync("r1", FdgContextPropertyProvider.DescriptionProperty, "   ");
            await script.SetAsync("r1", FdgContextPropertyProvider.DescriptionProperty, "Back again");
            await script.SetAsync("r1", FdgContextPropertyProvider.DescriptionProperty, "");
        },

        // B9: whether an entry with a key written twice can be edited.
        ["key-twice.fdg"] = async script =>
        {
            await script.ReadBackAsync("u1");
            await script.AddAsync(FdgElementTypes.UiElement, "second", 240, 160);
        },

        // B8: whether a body that is not YAML can be edited.
        ["not-yaml.fdg"] = async script =>
        {
            await script.ReadBackAsync("one");
            await script.AddAsync(FdgElementTypes.UiElement, "second", 240, 160);
        },
    };

    /// <summary>The probe of the fixture named <paramref name="fileName"/>, or null when it needs none.</summary>
    public static Func<FdgTranscript.Script, Task>? For(string fileName) => _probes.GetValueOrDefault(fileName);

    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }
}
