using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public partial class PropertiesWidget 
{
    [CascadingParameter] private DiagramContext Context { get; set; } = null!;

    protected override void OnInitialized()
    {
        Context.SelectionChanged += StateHasChanged;
    }

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
            Value = DateOnly.FromDateTime(DateTime.Now),
            ValueType = typeof(DateOnly),
        },
        new()
        {
            Key = "Link",
            Value = new Uri("https://github.com"),
            ValueType = typeof(Uri),
        },
        new()
        {
            Key = "Show link",
            Value = true,
            ValueType = typeof(bool),
        },
        new()
        {
            Key = "State",
            Value = new TagGroup { Id = TagGroupIdentifier.NewIdentifier(), Name = "State", },
            ValueType = typeof(TagGroup),
        },
        new()
        {
            Key = "Maturity",
            Value = new TagGroup { Id = TagGroupIdentifier.NewIdentifier(), Name = "Maturity", },
            ValueType = typeof(TagGroup),
        }
    ];
    
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private string _text = "234";

    [Parameter] public double Width { get; set; }
    [Parameter] public double Height { get; set; }
    [Parameter] public string? Style { get; set; }
}