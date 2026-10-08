// One file, several tiny namespaces: each namespace stands in for one module assembly, the
// same way the diagram and editor families' discovery fixtures do. The types are data for
// reflection tests, not code anyone calls.

// ReSharper disable UnusedMember.Global - Reason: every Definitions member in this file is read only by reflection - DesignerDefinitionDiscovery looks it up by name (DefinitionsPropertyName) on the fixture types DesignerDefinitionDiscovery.Tests hands it - so no code names these members directly.

namespace EtAlii.Adp.Designer.Tests.Fixtures.Valid
{
    public static class Designer
    {
        public static DesignerDefinition[] Definitions { get; } =
        [
            new("fixture/form", "Fixture form"),
        ];
    }
}

namespace EtAlii.Adp.Designer.Tests.Fixtures.Duplicate
{
    public static class Designer
    {
        public static DesignerDefinition[] Definitions { get; } =
        [
            new("fixture/form", "Duplicate of the fixture form"),
        ];
    }
}

namespace EtAlii.Adp.Designer.Tests.Fixtures.Ordering.Zulu
{
    public static class Designer
    {
        public static DesignerDefinition[] Definitions { get; } =
        [
            new("fixture/zulu", "Zulu"),
        ];
    }
}

namespace EtAlii.Adp.Designer.Tests.Fixtures.Ordering.Alpha
{
    public static class Designer
    {
        public static DesignerDefinition[] Definitions { get; } =
        [
            new("fixture/alpha", "Alpha"),
        ];
    }
}
