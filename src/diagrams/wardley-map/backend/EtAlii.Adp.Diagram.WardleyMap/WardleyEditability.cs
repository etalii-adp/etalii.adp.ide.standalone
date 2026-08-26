using Serilog;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Whether a map can be edited at all - the one place this module decides that
/// (Requirements 11.6, 13.5, 15.11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only mode, as it exists today.</b> `adp-diagram-ide` Requirement 5 describes a
/// read-only mode for a whole diagram, and this spec's Requirements 11.6, 13.5 and 15.11 each
/// say what this module owes it. That mode is <b>not implemented in core yet</b>: nothing in
/// the contract, the session or the history carries it, so a provider has no way to be told
/// about it. Inventing a signal here would be this module deciding a thing that is not its to
/// decide.
/// </para>
/// <para>
/// So this answers from the one thing that is real and observable now: whether the `.owm` can
/// be written. A file marked read-only - by source control, by a checkout, by the user - is a
/// map whose edits would be accepted, applied in memory and then quietly fail to reach disk,
/// which is precisely the disagreement between the menu and the outcome that Requirement 11.6
/// exists to prevent.
/// </para>
/// <para>
/// When core's read-only mode lands, <b>this method is the only place that has to learn it</b>.
/// That is why it exists as a named seam rather than as an inline attribute check in each of
/// the three providers.
/// </para>
/// </remarks>
public static class WardleyEditability
{
    private static readonly ILogger _logger = Log.ForContext(typeof(WardleyEditability));

    /// <summary>The reason an edit is refused, ready to be shown to a user.</summary>
    public const string Reason = "This map's file is read-only.";

    /// <summary>Whether the map at <paramref name="bodyPath"/> can be written.</summary>
    /// <remarks>
    /// A file that does not exist yet is editable: Requirement 2.4 opens a map whose `.owm`
    /// sibling is absent and creates it on the first save, so refusing every edit on it would
    /// make a new map permanently empty.
    /// </remarks>
    public static bool Editable(string bodyPath)
    {
        if (string.IsNullOrWhiteSpace(bodyPath))
        {
            return false;
        }

        try
        {
            return !File.Exists(bodyPath) || !File.GetAttributes(bodyPath).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unable to tell means the edit is offered and the save decides. Withholding every
            // action because one attribute read failed would be a worse answer than letting the
            // command say what went wrong.
            _logger.Warning(exception, "Could not read the attributes of {BodyPath}; treating it as editable", bodyPath);
            return true;
        }
    }
}
