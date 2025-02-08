using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;

namespace EtAlii.Adp.Api;

// ReSharper disable once ClassNeverInstantiated.Global
// Reason: This class is registered in the AdpDbContext.
public class DiagramPositionToPointConverter() : ValueConverter<DiagramPosition, Point>(v => (Point)v, v => (DiagramPosition)v)
{
}