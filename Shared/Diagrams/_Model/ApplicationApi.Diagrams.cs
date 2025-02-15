namespace EtAlii.Adp;

public static partial class ApplicationApi
{
    public static partial class Diagrams
    {
        public static class Get
        {
            public const string Request = $"/api/{Function}";
            public const string Function = "diagrams-get";
        }

        public static class Add
        {
            public const string Request = $"/api/{Function}";
            public const string Function = "diagrams-add";
            public const string Route = Function;
        }

        public static class Remove
        {
            public static string Request(DiagramIdentifier id) => $"/api/{Function}/{id.Identifier}";
            public const string Function = "diagrams-remove";
            public const string Route = $"{Function}/{{id:guid}}";
        }

        public static class Edit
        {
            public static string Request(DiagramIdentifier id) => $"/api/{Function}/{id.Identifier}";
            public const string Function = "diagrams-edit";
            public const string Route = $"{Function}/{{id:guid}}";
        }
    }
}