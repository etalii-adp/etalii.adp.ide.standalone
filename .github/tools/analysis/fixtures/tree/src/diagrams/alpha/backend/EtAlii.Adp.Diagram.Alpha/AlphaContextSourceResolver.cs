namespace EtAlii.Adp.Diagram;

// A resolver: the same in both modules.
public sealed class AlphaContextSourceResolver
{
    private readonly string _name = "Alpha resolver";

    public int Resolve(int value)
    {
        var total = value + 1;
        if (total > 10)
        {
            total -= 10; // "{ not a brace }"
        }
        var label = $"{_name}: {total}";
        System.Console.WriteLine(label);
        System.Console.WriteLine(_name);
        return total;
    }
}
