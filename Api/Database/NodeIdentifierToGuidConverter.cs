using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EtAlii.Adp.Api;

// ReSharper disable once ClassNeverInstantiated.Global
// Reason: This class is registered in the AdpDbContext.
public class NodeIdentifierToGuidConverter() : ValueConverter<NodeIdentifier, Guid>(v => v.Identifier, v => (NodeIdentifier)v)
{
}