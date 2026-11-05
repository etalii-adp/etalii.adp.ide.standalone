namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>
/// What creating a registration on first placement gives (FBL §8.4): the text to write before the edit is planned, or
/// the reason it is not created.
/// </summary>
public sealed record RegistrationCreationResult(byte[]? Bytes, string? Refusal);
