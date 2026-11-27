using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer;

/// <summary>
/// How a designer type recognises its document by the document's own content rather than by a registration (FBL sections
/// 12.1 to 12.3, spec 093 R1): made from the type's bundled bindings alone, so that a module names its bindings and nothing
/// else, and the host holds no extension, marker or origin for any one designer type. A file one of the bindings takes by
/// its name, and whose marker it matches, is the type's document whatever registration stands beside it, and opens without one.
/// </summary>
public sealed class DesignerClaim
{
    private readonly IReadOnlyList<FblBinding> _bindings;

    /// <param name="bindings">The type's bundled bindings, one per format its documents are kept in.</param>
    public DesignerClaim(IReadOnlyList<FblBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Count == 0)
        {
            throw new ArgumentException("A designer type's claim is made from at least one binding.", nameof(bindings));
        }

        _bindings = bindings;
        CreatesRegistration = bindings.All(BodyClaim.CreatesRegistration);
    }

    /// <summary>
    /// Whether the registration is created with the first setting the user changes (<c>createOnFirstPlacement</c>, FBL section
    /// 8.4), so a new document is its body alone and deleting the registration leaves the body.
    /// </summary>
    public bool CreatesRegistration { get; }

    /// <summary>Whether one of the bindings takes a file of this name by its <c>names</c> or <c>extensions</c>, before any marker is read.</summary>
    public bool Takes(string fileName) => _bindings.Any(binding => BodyClaim.Takes(binding, fileName));

    /// <summary>Whether a file of this name, whose head is the bytes given, is the type's document (FBL section 12.3).</summary>
    public bool Matches(string fileName, byte[] head) => BodyClaim.Claims(_bindings, fileName, head);

    /// <summary>
    /// The mark a file of this name needs to be the type's document, as a person reads it, for a refusal to name; null where
    /// the binding that takes the name has none to quote, as for a pattern.
    /// </summary>
    public string? Mark(string fileName) =>
        _bindings.Where(binding => BodyClaim.Takes(binding, fileName)).Select(BodyClaim.Mark).FirstOrDefault(mark => mark is not null);
}
