using System.Reflection;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The first layer of the no-writer proof: the reflection surface test. "There is no writer" is
/// the kind of guarantee that erodes the first time someone adds a convenience method, so this
/// test fails when a write-shaped member merely exists - before it is ever called. The second
/// layer (the behavioural byte-snapshot sweep) and the third (no toolbox, no mutating actions)
/// land with the integration tests and providers respectively.
/// </summary>
public class NoWriterSurfaceTests
{
    private static readonly Assembly _module = typeof(SparqlDocumentStore).Assembly;

    private static readonly string[] _writeShapedPrefixes = ["Save", "Write", "Flush", "Persist"];

    [Fact]
    public void TheModuleDeclaresNoCommandAndNoWriter()
    {
        // Arrange & act.
        var offenders = _module.GetTypes()
            .Where(type =>
                type.Name.Contains("Writer", StringComparison.Ordinal)
                || type.Name.Contains("Command", StringComparison.Ordinal)
                || type.GetInterfaces().Any(contract =>
                    contract.Name.StartsWith("ICommandHandler", StringComparison.Ordinal)
                    || contract.Name == "ICommand"))
            .Select(type => type.FullName)
            .ToList();

        // Assert.
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheStoreSurface_EndsAtReading()
    {
        // Arrange.
        var storeTypes = new[] { typeof(ISparqlDocumentStore), typeof(SparqlDocumentStore) };

        // Act.
        var offenders = new List<string>();
        foreach (var type in storeTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (_writeShapedPrefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    offenders.Add($"{type.Name}.{method.Name} is write-shaped by name");
                }

                if (method.ReturnType == typeof(Stream) || method.ReturnType == typeof(TextWriter)
                    || method.GetParameters().Any(parameter =>
                        typeof(Stream).IsAssignableFrom(parameter.ParameterType)
                        || typeof(TextWriter).IsAssignableFrom(parameter.ParameterType)))
                {
                    offenders.Add($"{type.Name}.{method.Name} is write-shaped by signature");
                }
            }
        }

        // Assert.
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheModuleReferencesNoHttpClient()
    {
        // Arrange & act.
        // SERVICE is drawn and never contacted; the design pins that no code path could contact
        // an endpoint because no HTTP client is referenced by the module at all.
        var references = _module.GetReferencedAssemblies().Select(name => name.Name).ToList();

        // Assert.
        Assert.DoesNotContain("System.Net.Http", references);
        Assert.DoesNotContain("System.Net.Requests", references);
    }
}
