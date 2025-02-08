namespace EtAlii.Adp;

public static partial class ApplicationPath
{
    public static partial class Diagrams
    {
        public const string GetDiagramsRequest = $"/api/{GetDiagramsFunction}";
        public const string GetDiagramsFunction = "diagrams-get";

        public const string AddDiagramRequest = $"/api/{AddDiagramFunction}";
        public const string AddDiagramFunction = "diagrams/add";

        public const string RemoveDiagramRequest = $"/api/{RemoveDiagramFunction}";
        public const string RemoveDiagramFunction = "diagrams/remove";

        public const string EditDiagramRequest = $"/api/{EditDiagramFunction}";
        public const string EditDiagramFunction = "diagrams/edit";
    }
}