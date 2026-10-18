using System.Reflection;
using Grpc.Core;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// No gRPC service is handed another: each delegates to domain services, so a call's behaviour is
/// written once and the tab's one stream runs the same code without composing the services whose
/// calls it replaces (code-structure FR-067). Checked on the compiled constructors of every service
/// the host can map, so a service taken in by a later change fails it whatever a comment says.
/// </summary>
public class GrpcServiceDelegationTests
{
    [Fact]
    public void NoGrpcService_TakesAnotherInItsConstructor()
    {
        // Arrange.
        var services = GrpcServices();

        // Act.
        var composed = services
            .SelectMany(service => service.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => IsGrpcService(parameter.ParameterType))
                .Select(parameter => $"{service.Name} takes {parameter.ParameterType.Name}"))
            .ToArray();

        // Assert: the services are found at all, the one stream among them, so a check that sees
        // none cannot pass for one that sees no composition.
        Assert.Contains(services, service => service.Name == "WorkspaceService");
        Assert.True(composed.Length == 0, "gRPC services composing gRPC services: " + string.Join("; ", composed));
    }

    /// <summary>Every gRPC service in the backend's own assemblies beside the tests.</summary>
    private static Type[] GrpcServices() =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, "EtAlii.Adp.*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal))
            .Select(path => Assembly.Load(Path.GetFileNameWithoutExtension(path)))
            .SelectMany(LoadableTypes)
            .Where(IsGrpcService)];

    /// <summary>A concrete class whose base is a generated service base, which carries <see cref="BindServiceMethodAttribute"/>.</summary>
    private static bool IsGrpcService(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.GetCustomAttribute<BindServiceMethodAttribute>() is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
