using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EtAlii.Adp.Api;

// ReSharper disable once ClassNeverInstantiated.Global
// Reason: This class is registered in the AdpDbContext.
public class TagIdentifierToGuidConverter() : ValueConverter<TagIdentifier, Guid>(v => v.Identifier, v => (TagIdentifier)v)
{
}