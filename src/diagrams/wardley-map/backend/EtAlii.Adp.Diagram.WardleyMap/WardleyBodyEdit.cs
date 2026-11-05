using System.Globalization;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The edits of one <c>.owm</c> body, each a <see cref="ModelChange"/> the library plans as splices through the module's
/// binding (FBL §6), and the ids the elements have after them (wardley-map-disl-fbl task 12).
/// </summary>
/// <remarks>
/// <para>
/// <b>No module code builds a statement's text or replaces a line</b> (Requirement 5.1). One value changed is only its
/// bytes changed, so a trailing comment, an unknown decorator, a label offset and the author's spacing survive; a new
/// statement goes where the binding's <c>insert</c> places it, and with the line ending FBL gives it (finding B12).
/// </para>
/// <para>
/// <b>The ids move with their keys</b> (finding B10, Requirement 6.3): the body is opened as an <see cref="IdentifiedBody"/>
/// over the identities the store holds for it, so a rename moves the key of the element and of every link, pipeline and
/// pipeline component whose key holds its name, a removal takes its entries away, and an add stores the id it gives, each
/// in the edit that makes it (FBL 0.12 §8.6). Those identities are the store's, written where the map keeps them only when
/// the edit is saved (<see cref="WardleyIdentityFiles"/>), so the file they live in is opened here as their copy.
/// </para>
/// <para>
/// <b>Undo stays the host's</b>: the shared restore command puts the whole text back, and the store the identities it had
/// for that text. The library's own history of this body is not used.
/// </para>
/// </remarks>
public sealed class WardleyBodyEdit
{
    private readonly IdentifiedBody _body;
    private readonly List<Splice> _splices = [];

    internal WardleyBodyEdit(string text, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        var copy = JsonSerializer.SerializeToUtf8Bytes(identities.Select(entry => new { id = entry.Id, kind = entry.Kind, key = entry.Key }));
        _body = IdentifiedBody.Open(Encoding.UTF8.GetBytes(text), WardleyBody.Binding, WardleyBody.Options, LegacySidecar.Open(copy));
    }

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Body.Bytes);

    /// <summary>Every splice planned so far, in order: what the tests read to see how an edit was written.</summary>
    internal IReadOnlyList<Splice> Splices => _splices;

    /// <summary>What the binding reads from the body now, under the ids its elements have now.</summary>
    public FblModel Reading => _body.Body.Model;

    /// <summary>The identities of the body's elements now, in the order <see cref="WardleyMap.Holders"/> gives them.</summary>
    public IReadOnlyList<WardleyIdentityEntry> Identities() =>
        WardleyBody.Identify(LineDocument.Parse(Text), _body.Identities.Identities());

    /// <summary>Plans and applies <paramref name="change"/>; the library's sentence when it refuses it.</summary>
    /// <remarks>
    /// An add writes its statement as the binding's <c>insert</c> emits it, which leaves out a part the statement may
    /// have, such as a component's decorators or its inertia; each such part it was given is then set on the new element,
    /// in the same edit, as its own attribute's <c>create</c> writes it. An add is refused while a pipeline's block is
    /// unclosed, unless it is a pipeline component, which goes into its own pipeline: every other statement is written at
    /// the end of the document, which is inside that block (ruling 1(a) of 2026-11-02).
    /// </remarks>
    public EditOutcome Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        change = Positioned(change);
        if (change is not ModelChange.Add add)
        {
            return Planned(change);
        }

        if (add.Type != "PipelineComponent" && UnclosedPipeline() is { } parent)
        {
            return EditOutcome.Refused($"'{parent}' has a pipeline that is missing its closing brace, so nothing can be added after it.");
        }

        var before = Reading.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        if (Planned(add) is { WasApplied: false } refused)
        {
            return refused;
        }

        var added = Reading.Elements.FirstOrDefault(element => add.Id is not null && element.Id == add.Id)
            ?? Reading.Elements.FirstOrDefault(element => element.Type == add.Type && !before.Contains(element.Id));
        if (added is null)
        {
            return EditOutcome.Applied;
        }

        var missing = add.Attributes
            .Where(attribute => Words(attribute.Value) is { } wanted && !wanted.SequenceEqual(Words(added.Attributes.GetValueOrDefault(attribute.Key)) ?? [], StringComparer.Ordinal))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        return missing.Count == 0 ? EditOutcome.Applied : Planned(new ModelChange.Set(added.Id, missing));
    }

    /// <summary>
    /// The id the binding reads <paramref name="element"/> of the model under: its identity for an element that keeps one,
    /// and for one that keeps none, an evolution, the id the binding gives the statement on its line; null for none.
    /// </summary>
    public string? ReadingIdOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (Reading.Elements.Any(read => read.Id == element.Id))
        {
            return element.Id;
        }

        return element.IdIsStored || element.Line is not { } line
            ? null
            : Reading.Elements.FirstOrDefault(read => read.IdentityKind is null && read.Line == line)?.Id;
    }

    /// <summary>
    /// <paramref name="change"/> with an annotation's positions rounded as every other coordinate is written, to four
    /// decimals, halves away from zero (Requirement 5.4): the binding's <c>positions</c> items are structured values, which
    /// it writes as it is given them.
    /// </summary>
    private static ModelChange Positioned(ModelChange change) => change switch
    {
        ModelChange.Add add when add.Attributes.TryGetValue("positions", out var positions) => add with { Attributes = Rounded(add.Attributes, positions) },
        ModelChange.Set set when set.Attributes.TryGetValue("positions", out var positions) => set with { Attributes = Rounded(set.Attributes, positions) },
        _ => change,
    };

    private static Dictionary<string, object?> Rounded(IReadOnlyDictionary<string, object?> attributes, object? positions)
    {
        var rounded = new Dictionary<string, object?>(attributes, StringComparer.Ordinal);
        if (positions is IEnumerable<object?> items)
        {
            rounded["positions"] = items.Select(item => item is IReadOnlyDictionary<string, object?> position
                ? position.ToDictionary(part => part.Key, part => part.Value is double number ? Rounded(number) : part.Value, StringComparer.Ordinal)
                : item).ToList();
        }

        return rounded;
    }

    /// <summary>A number rounded to four decimals as the decimal it is written as, so that 0.12345 is a half.</summary>
    private static double Rounded(double number) =>
        decimal.TryParse(number.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var written)
            ? (double)Math.Round(written, 4, MidpointRounding.AwayFromZero)
            : Math.Round(number, 4, MidpointRounding.AwayFromZero);

    /// <summary>The component whose pipeline's block runs to the end of the body unclosed, or null for none.</summary>
    private string? UnclosedPipeline() =>
        Reading.Findings
            .Where(finding => finding is { Code: "fbl.unclosed-block", Location: not null })
            .Select(finding => Reading.Elements.FirstOrDefault(element => element.Type == "Pipeline" && element.Line == finding.Location!.Line))
            .Select(pipeline => pipeline?.Attributes.GetValueOrDefault("component") as string)
            .FirstOrDefault(name => name is not null);

    private EditOutcome Planned(ModelChange change)
    {
        switch (_body.Change(change).Body)
        {
            case PlanResult.Refused refused:
                return EditOutcome.Refused(refused.Reason);
            case PlanResult.Planned planned:
                _splices.AddRange(planned.Edit.Splices);
                break;
        }

        return EditOutcome.Applied;
    }

    /// <summary>A text value as its one word, a flag as its word, a list of texts as its words; null for any other value, which an add's emit writes itself.</summary>
    private static IReadOnlyList<string>? Words(object? value) => value switch
    {
        string text => [text],
        bool flag => [flag ? "true" : "false"],
        IEnumerable<object?> items => items.ToList() is var list && list.All(item => item is string) ? [.. list.Cast<string>()] : null,
        _ => null,
    };
}
