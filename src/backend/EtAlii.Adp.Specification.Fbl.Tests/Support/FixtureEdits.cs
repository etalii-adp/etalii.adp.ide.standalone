using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>The parts of a round-trip fixture's steps that every runner reads alike (FBL 0.7 §15.3): splices, settings and values.</summary>
internal static class FixtureEdits
{
    private static Splice Splice(JsonElement splice) => new(
        Fbl.Splice.Parse(splice.GetProperty("operation").GetString()!),
        splice.GetProperty("start").GetInt32(),
        splice.GetProperty("end").GetInt32(),
        splice.GetProperty("text").GetString()!);

    /// <summary>
    /// A step's splices in body order, those at one offset kept as listed: FBL gives an order only to splices at the
    /// same offset (section 6.5), and a fixture written since 0.3 lists a cascaded removal bottom-up, as it is planned,
    /// where this library lists every edit in body order. The two say the same edit.
    /// </summary>
    public static List<Splice> Splices(JsonElement step) =>
        step.GetProperty("splices").EnumerateArray().Select(Splice)
            .Select((splice, at) => (splice, at)).OrderBy(listed => listed.splice.Start).ThenBy(listed => listed.at).Select(listed => listed.splice).ToList();

    /// <summary>A fixture's change to an entry of a block the binding declares (FBL 0.7 §8.4, §15.3).</summary>
    public static ModelChange.Setting Setting(JsonElement setting)
    {
        SettingPosition? position = null;
        if (setting.TryGetProperty("position", out var at))
        {
            position = new SettingPosition(
                at.TryGetProperty("index", out var index) ? index.GetInt32() : null,
                at.TryGetProperty("after", out var after) ? after.GetString() : null,
                at.TryGetProperty("before", out var before) ? before.GetString() : null);
        }
        return new ModelChange.Setting(
            setting.GetProperty("block").GetString()!,
            setting.GetProperty("key").GetString()!,
            setting.TryGetProperty("value", out var value) ? value.GetString() : null,
            position,
            setting.TryGetProperty("rename", out var rename) ? rename.GetString() : null,
            setting.TryGetProperty("remove", out var remove) && remove.GetBoolean());
    }

    public static Dictionary<string, object?> Attributes(JsonElement attributes) =>
        attributes.EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value), StringComparer.Ordinal);

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(Value).ToList(),
        _ => null,
    };
}
