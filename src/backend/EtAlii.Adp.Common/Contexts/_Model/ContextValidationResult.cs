namespace EtAlii.Adp.Common;

/// <summary>
/// A provider's verdict on a proposed value. The same verdict drives the dialog's
/// confirm button and the re-check performed just before committing, so the two
/// cannot drift apart.
/// </summary>
public sealed record ContextValidationResult(bool Valid, string Reason = "")
{
    public static ContextValidationResult Accepted { get; } = new(true);

    public static ContextValidationResult Rejected(string reason) => new(false, reason);
}
