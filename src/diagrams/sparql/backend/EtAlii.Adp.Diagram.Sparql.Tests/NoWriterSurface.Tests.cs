using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Xml;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The first layer of the no-writer proof: the reflection surface test. "There is no writer" is
/// the kind of guarantee that erodes the first time someone adds a convenience method, so this
/// test fails when a write-shaped member merely exists - before it is ever called. The second
/// layer (the behavioural byte-snapshot sweep) and the third (no toolbox, no mutating actions)
/// land with the integration tests and providers respectively.
/// <para>
/// The empty document the module registers is deliberately not caught by any of this, and the
/// distinction is the whole point: it hands core the text of a file being created, and nothing
/// in this module can rewrite a query that already exists.
/// </para>
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
        // Every interface the store's own one extends as well, because the shared store's surface is
        // inherited rather than declared here: extending the writable store instead of its reading
        // half would bring a save in without a line of this module changing.
        var storeTypes = typeof(ISparqlDocumentStore).GetInterfaces()
            .Append(typeof(ISparqlDocumentStore))
            .Append(typeof(SparqlDocumentStore))
            .ToArray();

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

    /// <summary>The types a write goes through: a stream, a writer, or a body's own save and change.</summary>
    private static readonly Type[] _writerTypes = [typeof(Stream), typeof(TextWriter), typeof(BinaryWriter), typeof(XmlWriter), typeof(Utf8JsonWriter)];

    /// <summary>The members of a spliced body that change or write its bytes.</summary>
    private static readonly string[] _bodyWrites = ["Save", "Change", "Apply", "Group", "Undo", "Redo"];

    private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void NoCodeInTheModule_PlansASplice_WritesABody_OrHoldsAWriter()
    {
        // Arrange: every method body of every type, compiler-generated ones included (lambdas, iterators and async
        // state machines are types of their own), walked instruction by instruction, so that a write reached by any
        // name is found - the plugin's plan, the store, the session and whatever is added beside them.
        var offenders = new List<string>();

        // Act.
        foreach (var type in _module.GetTypes())
        {
            foreach (var field in type.GetFields(Everything).Where(field => IsWriter(field.FieldType)))
            {
                offenders.Add($"{type.FullName}.{field.Name} holds a {field.FieldType.Name}");
            }

            foreach (var method in type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)))
            {
                var at = $"{type.FullName}.{method.Name}";
                if (method is MethodInfo info && (IsWriter(info.ReturnType) || IsSpliceList(info.ReturnType)))
                {
                    offenders.Add($"{at} returns a {info.ReturnType.Name}");
                }

                offenders.AddRange(method.GetParameters().Where(parameter => IsWriter(parameter.ParameterType)).Select(parameter => $"{at} takes a {parameter.ParameterType.Name}"));
                offenders.AddRange(Called(method).Select(Offence).OfType<string>().Select(offence => $"{at} {offence}"));
            }
        }

        // Assert.
        Assert.Empty(offenders);
    }

    private static bool IsWriter(Type type) =>
        _writerTypes.Any(writer => writer.IsAssignableFrom(type.IsByRef ? type.GetElementType()! : type));

    private static bool IsSpliceList(Type type) =>
        type.IsGenericType && typeof(IEnumerable<PluginSplice>).IsAssignableFrom(type);

    private static string? Offence(MethodBase called)
    {
        var owner = called.DeclaringType;
        if (called is ConstructorInfo && (owner == typeof(PluginPlanResult.Planned) || owner == typeof(PluginSplice)))
        {
            return $"creates a {owner.Name}";
        }

        if (owner is not null && typeof(SplicedFile).IsAssignableFrom(owner) && _bodyWrites.Contains(called.Name))
        {
            return $"calls {owner.Name}.{called.Name}";
        }

        return null;
    }

    /// <summary>Every method <paramref name="method"/>'s body calls, creates or takes the address of.</summary>
    private static IEnumerable<MethodBase> Called(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < il.Length)
        {
            var code = il[position] == 0xFE ? _opCodes[(short)(0xFE00 | il[position + 1])] : _opCodes[il[position]];
            position += code.Size;
            if (code.OperandType == OperandType.InlineMethod)
            {
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, position), typeArguments, methodArguments)!;
            }

            position += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
                _ => 4,
            };
        }
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
