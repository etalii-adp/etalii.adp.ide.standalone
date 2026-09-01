// One file, several tiny namespaces: each namespace stands in for one module assembly, the
// same way the diagram family's discovery fixtures do. The types are data for reflection
// tests, not code anyone calls.

// ReSharper disable UnusedMember.Global
#pragma warning disable CA1052 // "static holder" - NotStatic is deliberately not static

namespace EtAlii.Adp.Editor.Tests.Fixtures.Valid
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } =
        [
            new("valid", "Valid Fixture", Extensions: [".txt"]),
        ];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Several
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } =
        [
            new("several-one", "First of Several"),
            new("several-two", "Second of Several"),
        ];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Duplicate
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } =
        [
            new("valid", "Duplicate of Valid"),
        ];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Ordering.Zulu
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } = [new("zulu", "Zulu")];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Ordering.Alpha
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } = [new("alpha", "Alpha")];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NoProperty
{
    public static class Editor;
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.WrongType
{
    public static class Editor
    {
        public static string Definitions => "not a sequence";
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NullEntry
{
    public static class Editor
    {
        public static EditorDefinition?[] Definitions { get; } = [new("kept", "Kept"), null];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.EmptyArray
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions { get; } = [];
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.Throws
{
    public static class Editor
    {
        public static EditorDefinition[] Definitions => throw new InvalidOperationException("deliberately broken");
    }
}

namespace EtAlii.Adp.Editor.Tests.Fixtures.Malformed.NotStatic
{
    public sealed class Editor
    {
        public static EditorDefinition[] Definitions { get; } = [new("not-static", "Not Static")];
    }
}
