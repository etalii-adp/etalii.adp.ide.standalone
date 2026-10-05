using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The DISL function library (§12.4) and the element methods of §12.2 that this runtime offers, on
/// top of CEL's standard environment. A specification that calls anything else is refused at load,
/// naming it.
/// </summary>
/// <remarks>
/// The element methods are declared only: <c>isA</c>, <c>descendants</c> and the rest are
/// implemented by the model's elements (<see cref="ICelObject"/>), and calling one on any other value
/// is an evaluation error.
/// </remarks>
public static class DislCelLibrary
{
    /// <summary>The methods of DISL's Element and Diagram types (§12.2) this runtime implements, with their arities.</summary>
    private static readonly (string Name, int Min, int Max)[] Methods =
    [
        ("isA", 1, 1),
        ("descendants", 0, 0),
        ("ancestors", 0, 0),
        ("childrenOfType", 1, 1),
        ("incomingOf", 1, 1),
        ("outgoingOf", 1, 1),
        ("positionIn", 1, 1),
        ("nodesOfType", 1, 2),
        ("relationsOfType", 1, 1),
        ("elementById", 1, 1),
        ("label", 0, 0),
        ("other", 1, 1),
    ];

    /// <summary>Adds the library to <paramref name="environment"/>; <paramref name="enums"/> finds the enumerations <c>enumLabel</c> reads.</summary>
    public static CelEnvironment Register(CelEnvironment environment, Func<string, DislEnum?> enums)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(enums);

        foreach (var (name, min, max) in Methods)
        {
            environment.AddFunction(CelFunction.Method(name, min, max));
        }

        environment.AddFunction(Pending("enumLabel", 2, 2));
        environment.AddFunction(Pending("clamp", 3, 3));
        environment.AddFunction(Pending("min", 2, 4));
        environment.AddFunction(Pending("max", 2, 4));
        environment.AddFunction(Pending("yearMonth", 2, 2));
        environment.AddFunction(Pending("formatYearMonth", 2, 2));
        environment.AddFunction(Pending("parseYearMonth", 1, 1));
        environment.AddFunction(new CelFunction("year", CelCallStyle.Receiver, 0, 0, _ => throw NotYet("year")));
        environment.AddFunction(new CelFunction("month", CelCallStyle.Receiver, 0, 0, _ => throw NotYet("month")));
        return environment;
    }

    private static CelFunction Pending(string name, int min, int max) =>
        new(name, CelCallStyle.Global, min, max, _ => throw NotYet(name));

    private static CelException NotYet(string name) => new($"'{name}()' is declared but not yet implemented by this runtime.");
}
