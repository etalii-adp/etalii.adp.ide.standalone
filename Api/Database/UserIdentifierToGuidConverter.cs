using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EtAlii.Adp.Api;

// ReSharper disable once ClassNeverInstantiated.Global
// Reason: This class is registered in the AdpDbContext.
public class UserIdentifierToGuidConverter() : ValueConverter<UserIdentifier, Guid>(v => v.Identifier, v => (UserIdentifier)v)
{
}