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

    internal WardleyBodyEdit(string text, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        var copy = JsonSerializer.SerializeToUtf8Bytes(identities.Select(entry => new { id = entry.Id, kind = entry.Kind, key = entry.Key }));
        _body = IdentifiedBody.Open(Encoding.UTF8.GetBytes(text), WardleyBody.Binding, WardleyBody.Options, LegacySidecar.Open(copy));
    }

    /// <summary>The body's text after every change made to it.</summary>
    public string Text => Encoding.UTF8.GetString(_body.Body.Bytes);

    /// <summary>What the binding reads from the body now, under the ids its elements have now.</summary>
    public FblModel Reading => _body.Body.Model;

    /// <summary>The identities of the body's elements now, in the order <see cref="WardleyMap.Holders"/> gives them.</summary>
    public IReadOnlyList<WardleyIdentityEntry> Identities() =>
        WardleyBody.Identify(LineDocument.Parse(Text), _body.Identities.Identities());

    /// <summary>Plans and applies <paramref name="change"/>; the library's sentence when it refuses it.</summary>
    /// <remarks>
    /// An add writes its statement as the binding's <c>insert</c> emits it, which leaves out a part the statement may
    /// have, such as a component's decorators or its inertia; each such part it was given is then set on the new element,
    /// in the same edit, as its own attribute's <c>create</c> writes it.
    /// </remarks>
    public EditOutcome Change(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change is not ModelChange.Add add)
        {
            return Planned(change);
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

    private EditOutcome Planned(ModelChange change) =>
        _body.Change(change).Body is PlanResult.Refused refused ? EditOutcome.Refused(refused.Reason) : EditOutcome.Applied;

    /// <summary>A text value as its one word, a list of texts as its words; null for any other value, which an add's emit writes itself.</summary>
    private static IReadOnlyList<string>? Words(object? value) => value switch
    {
        string text => [text],
        IEnumerable<object?> items when items.All(item => item is string) => [.. items.Cast<string>()],
        _ => null,
    };
}
