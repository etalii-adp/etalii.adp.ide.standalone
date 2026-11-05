namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>What a test reads off a knowledge table by id: one view, one property, one cell, or the values a cell holds.</summary>
internal static class KnowledgeTableExtensions
{
    extension(KnowledgeTable table)
    {
        /// <summary>The view <paramref name="id"/>.</summary>
        public KnowledgeView View(string id) => table.Views.Single(view => view.Id == id);

        /// <summary>The property <paramref name="id"/>.</summary>
        public KnowledgeProperty Property(string id) => table.Properties.Single(property => property.Id == id);

        /// <summary>The cell of row <paramref name="rowId"/> for property <paramref name="propertyId"/>, which must be there.</summary>
        public KnowledgeCell Cell(string rowId, string propertyId) => table.Rows.Single(row => row.Id == rowId).Cells.Single(cell => cell.PropertyId == propertyId);

        /// <summary>The values row <paramref name="rowId"/> holds for property <paramref name="propertyId"/>, or null when it holds none.</summary>
        public IReadOnlyList<string>? ValuesOf(string rowId, string propertyId) => table.Rows.Single(row => row.Id == rowId).Cells.FirstOrDefault(cell => cell.PropertyId == propertyId)?.Values;
    }
}
