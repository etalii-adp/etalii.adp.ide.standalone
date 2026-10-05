using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// An element as a finding about it names it: every member the element's own, but <c>id</c> the id
/// it is written with, which for a later holder of a written id differs from its ephemeral model id
/// (§11.5.4). It compares equal to the element it shows, both ways round.
/// </summary>
internal sealed class DislWrittenElement(DislElement element, string writtenId) : ICelObject
{
    public DislElement Element { get; } = element;

    /// <inheritdoc />
    public bool TryGetMember(string name, out object? value)
    {
        if (name == "id" && !Element.Type.Attributes.ContainsKey("id"))
        {
            value = writtenId;
            return true;
        }
        return Element.TryGetMember(name, out value);
    }

    /// <inheritdoc />
    public bool HasMember(string name) => Element.HasMember(name);

    /// <inheritdoc />
    public bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value) => Element.TryInvoke(name, arguments, out value);

    public override bool Equals(object? obj) => ReferenceEquals(obj, Element) || obj is DislWrittenElement other && ReferenceEquals(other.Element, Element);

    public override int GetHashCode() => Element.GetHashCode();

    public override string ToString() => $"{Element.Type.Name} {writtenId}";
}
