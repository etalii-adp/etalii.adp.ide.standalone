using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class PropertiesWidget //: IDisposable
{
    [Parameter] public PropertyItem[] Properties { get; set; } =
    [
        new()
        {
            Key = "Name",
            Value = "test 1",
            ValueType = typeof(string),
        },
        new()
        {
            Key = "Description",
            Value = "Lorem ipsum dolor, sit amet, consectetur adipiscing elit.",
            ValueType = typeof(string),
        },
        new()
        {
            Key = "Created",
            Value = DateTime.Now,
            ValueType = typeof(DateTime),
        }
    ];
    
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private string _text = "234";

    [Parameter] public double Width { get; set; }
    [Parameter] public double Height { get; set; }
    [Parameter] public string? Style { get; set; }
}