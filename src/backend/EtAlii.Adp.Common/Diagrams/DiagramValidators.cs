using System.Reflection;

namespace EtAlii.Adp.Common;

/// <summary>
/// The registered <see cref="IDiagramValidator"/> instances, looked up by origin - how core
/// finds a type's rules without naming the type, mirroring <see cref="DiagramDocumentFactories"/>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Verify</c> counterpart here: a definition without a validator
/// is a type without rules, which is legitimate - unlike a definition without a document
/// factory, which could never create a file.
/// </remarks>
public sealed class DiagramValidators
{
    private readonly IReadOnlyDictionary<DiagramOrigin, IDiagramValidator> _byOrigin;

    public DiagramValidators(IEnumerable<IDiagramValidator> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);

        var byOrigin = new Dictionary<DiagramOrigin, IDiagramValidator>();
        foreach (var validator in validators)
        {
            if (byOrigin.TryGetValue(validator.Origin, out var existing))
            {
                // Two modules claiming one origin is a deployment error: silently picking
                // either would validate documents with rules their type never agreed to.
                throw new InvalidOperationException(
                    $"Two validators are registered for '{validator.Origin.Key}': " +
                    $"{existing.GetType().FullName} and {validator.GetType().FullName}.");
            }
            byOrigin.Add(validator.Origin, validator);
        }
        _byOrigin = byOrigin;
    }

    /// <summary>The validator for <paramref name="origin"/>, when its type has rules at all.</summary>
    public bool TryGet(DiagramOrigin origin, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IDiagramValidator? validator) =>
        _byOrigin.TryGetValue(origin, out validator);

    /// <summary>
    /// The version of the rules that judge <paramref name="origin"/>'s documents - the
    /// validator's own assembly's informational version, so a module release invalidates
    /// only its own cached verdicts. Empty when the type has no validator: no rules, no
    /// version to go stale against.
    /// </summary>
    public string RulesVersion(DiagramOrigin origin)
    {
        if (!_byOrigin.TryGetValue(origin, out var validator))
        {
            return "";
        }
        var assembly = validator.GetType().Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "";
    }
}
