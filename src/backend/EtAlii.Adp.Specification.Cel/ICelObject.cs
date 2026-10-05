namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// A host value an expression can work with as it does with a map: select a member (<c>e.name</c>,
/// <c>e.?name</c>), test one with <c>has(e.name)</c>, and call a method on it (<c>e.isA('Trend')</c>).
/// A method is called on the object first; a function the environment registers under the same name
/// for receivers is the fallback. Lists of these work with every macro.
/// </summary>
public interface ICelObject
{
    /// <summary>The member's value, false when the object has no such member.</summary>
    bool TryGetMember(string name, out object? value);

    /// <summary>Whether <c>has()</c> holds for the member: present, as a map key is present.</summary>
    bool HasMember(string name);

    /// <summary>
    /// The result of the method, false when the object does not implement it; <paramref name="arguments"/>
    /// exclude the object itself. Throw <see cref="CelException"/> for a call it implements but cannot
    /// complete.
    /// </summary>
    bool TryInvoke(string name, IReadOnlyList<object?> arguments, out object? value);
}
