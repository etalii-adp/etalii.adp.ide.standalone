using Xunit;

namespace EtAlii.Adp.Backend.Tests;

// ReSharper disable once NotAccessedPositionalProperty.Local
internal sealed record CommandResultSampleCommand(string Value) : ICommand;
