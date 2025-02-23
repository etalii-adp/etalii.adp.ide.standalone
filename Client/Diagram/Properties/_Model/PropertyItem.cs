namespace EtAlii.Adp.Client;

public class PropertyItem
{
    public string Key { get; set; } = string.Empty;
    public object? Value { get; set; }
    public Type ValueType { get; set; } = typeof(string);
    public List<string>? Options { get; set; } // For dropdowns
}
