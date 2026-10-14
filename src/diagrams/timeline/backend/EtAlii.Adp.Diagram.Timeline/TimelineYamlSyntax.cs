using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Whether a body is YAML, and if not, why and where, in the words and at the line the timeline has
/// always reported it (timeline-disl-fbl Requirement 4.2, ruled by the user in chat, 2026-10-11).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for what FBL leaves to each host</b>: FBL 7.5 lets the host's YAML reader word the
/// refusal of a body that is not YAML and decide which bodies it refuses. The sentence of
/// <c>timeline.unparseable</c> is YamlDotNet's, and so is the refusal of a mapping that holds one key
/// twice (finding B10), so YamlDotNet is asked first, by the store, the validator and the commands,
/// and a body it refuses never reaches <see cref="TimelineBody"/>. Whatever it accepts is read by the binding alone.
/// </para>
/// <para>
/// YamlDotNet is used here for that question only; nothing is read into a model or written through it.
/// The class goes when the findings FBL would add (Q2) are ruled on.
/// </para>
/// </remarks>
public static class TimelineYamlSyntax
{
    /// <summary>Null when <paramref name="text"/> is YAML; otherwise the reader's message and the 1-based line it stopped at.</summary>
    public static (string Message, int Line)? Check(string text)
    {
        try
        {
            Require(text);
            return null;
        }
        catch (YamlException exception)
        {
            return (exception.Message, (int)Math.Max(exception.Start.Line, 1));
        }
    }

    /// <summary>
    /// Throws YamlDotNet's own exception when <paramref name="text"/> is not YAML: what a command
    /// meets on a body that is not, as it always has.
    /// </summary>
    /// <exception cref="YamlException">The text is not well-formed YAML.</exception>
    public static void Require(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        new YamlStream().Load(reader);
    }
}
