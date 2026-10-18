namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The bundled definitions this test project embeds, under the logical names the modules embed them by.</summary>
internal static class Resources
{
    public const string HypeCycle = "gartner-hype-cycle-graph.dis";

    public const string BehaviorModel = "agent-behavior-modelling.dis";

    /// <summary>A test definition of the origin <c>example/wide</c>: boxes and notes.</summary>
    public const string WideReading = "wide-reading.dis";

    /// <summary>A test definition of the origin <c>example/narrow</c>, over the same metamodel: notes only.</summary>
    public const string NarrowReading = "narrow-reading.dis";

    public static System.Reflection.Assembly Assembly => typeof(Resources).Assembly;

    public static string Text(string logicalName)
    {
        using var stream = Assembly.GetManifestResourceStream(logicalName) ?? throw new InvalidOperationException($"No resource {logicalName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
