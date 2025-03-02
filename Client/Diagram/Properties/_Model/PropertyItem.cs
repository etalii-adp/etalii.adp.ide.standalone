namespace EtAlii.Adp.Client;

public class PropertyItem
{
    public string Key { get; init; } = string.Empty;
    public object Value { get; set; } = null!;

    public string ValueAsString
    {
        get => (string)Value;
        set => Value = value;
    }

    public int ValueAsInt
    {
        get => (int)Value;
        set => Value = value;
    }

    public float ValueAsSingle
    {
        get => (float)Value;
        set => Value = value;
    }

    public DateOnly ValueAsDateOnly
    {
        get => (DateOnly)Value;
        set => Value = value;
    }

    public bool ValueAsBoolean
    {
        get => (bool)Value;
        set => Value = value;
    }

    
    public Type ValueType { get; set; } = typeof(string);
    public List<string>? Options { get; set; } // For dropdowns
}
