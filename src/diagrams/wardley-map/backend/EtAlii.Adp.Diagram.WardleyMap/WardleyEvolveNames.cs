using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The names an `evolve` cannot be written with yet: one whose last word is a number, as a later holder's name is
/// (ruling 4). `evolve Tea 2 0.9` is a name and a maturity the binding's grammar cannot tell apart in LALR(1), so it reads
/// the evolution of `Tea` (spec 071, the coordinator's ruling (b) of 2026-11-02, a known limitation).
/// </summary>
/// <remarks>
/// Every check writes the evolution through the binding and reads it back, as <see cref="WardleyEdit.Unusable"/> does for
/// a link, rather than testing the last word: the day the binding reads such a name, the refusal lifts by itself, and
/// this class and its calls can be removed.
/// </remarks>
internal static class WardleyEvolveNames
{
    /// <summary>Why an evolution of <paramref name="name"/> cannot be written, or null when it can.</summary>
    public static string? OfComponent(string name) =>
        ReadsBack(name, "")
            ? null
            : $"'{name}' ends in a number, so an evolution of it cannot be written.";

    /// <summary>Why an evolution cannot arrive under <paramref name="becomes"/>, or null when it can.</summary>
    public static string? Arriving(string becomes) =>
        becomes.Length == 0 || ReadsBack("Other", becomes)
            ? null
            : $"'{becomes}' ends in a number, so an evolution cannot arrive under it.";

    /// <summary>Whether an evolution of <paramref name="name"/> arriving under <paramref name="becomes"/>, written through the binding, is read back as written.</summary>
    private static bool ReadsBack(string name, string becomes)
    {
        var body = WardleyBody.Edit(LineDocument.Parse(""), []);
        var component = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name, ["visibility"] = 0.5d, ["maturity"] = 0.5d };
        var evolve = new Dictionary<string, object?>(StringComparer.Ordinal) { ["component"] = name, ["maturity"] = 0.9d };
        if (becomes.Length > 0) evolve["becomes"] = becomes;
        var written = body.Change(new ModelChange.Add("Component", "readback:component", component)).WasApplied
            && body.Change(new ModelChange.Add("Evolve", null, evolve)).WasApplied;
        return written
            && WardleyBody.Read(LineDocument.Parse(body.Text)).Model.Evolves is [var read]
            && read.Name == name && read.Override == becomes && read.Maturity == 0.9d;
    }
}
