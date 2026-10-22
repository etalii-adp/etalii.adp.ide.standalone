using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Text;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// Creates a registration beside a body the first time the user places an element or changes a setting a declared block
/// keeps, when the binding's <c>registration.createOnFirstPlacement</c> is true (FBL §8.4, 0.7 §8.4).
/// </summary>
internal static class RegistrationCreation
{
    /// <summary>
    /// The text of the registration <paramref name="registrationName"/> to write beside the body <paramref name="bodyName"/>
    /// before the edit is planned, outside the history: the origin line, then <c>body: &lt;name&gt;</c> when the base names
    /// differ, in the body's dominant line ending. Refused with a reason when the binding does not create one, or when a
    /// file of that name exists (<paramref name="existing"/>) that is not a registration of this origin and body.
    /// </summary>
    /// <param name="bodyName">The body's path relative to the registration's folder, <c>/</c>-separated.</param>
    public static RegistrationCreationResult Create(FblBinding binding, string origin, string registrationName, string bodyName, byte[] body, byte[]? existing)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(body);
        if (!binding.Registration.CreateOnFirstPlacement)
        {
            return new RegistrationCreationResult(null, $"'{bodyName}' has no registration, and its tool type keeps its view data only in one, so the change cannot be kept.");
        }
        var named = !string.Equals(BaseName(registrationName), BaseName(bodyName), StringComparison.Ordinal);
        if (existing is not null)
        {
            var document = RegistrationDocument.Read(existing);
            var same = document.Origin == origin && (document.Body ?? (named ? null : bodyName)) == bodyName;
            return same
                ? new RegistrationCreationResult(existing, null)
                : new RegistrationCreationResult(null, $"'{registrationName}' exists and is no registration of '{bodyName}', so none is created beside it.");
        }
        var newline = new BodyText(body).DominantEnding ?? binding.Text.Newline;
        var text = origin + newline + (named ? $"body: {bodyName}{newline}" : "");
        return new RegistrationCreationResult(Encoding.UTF8.GetBytes(text), null);
    }

    private static string BaseName(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);
}
