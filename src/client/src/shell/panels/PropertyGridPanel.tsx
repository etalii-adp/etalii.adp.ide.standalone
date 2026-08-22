import { EntryKind } from "../../generated/hierarchy_pb";
import { ContextSelectionSource, type ContextLevelDetail, type ContextSelection } from "../../generated/context_pb";
import { useContextSelection } from "../context/ContextConnectionProvider";

const SOURCE_LABELS: Record<number, string> = {
  [ContextSelectionSource.EXPLORER]: "Explorer",
  [ContextSelectionSource.DIAGRAM_CANVAS]: "Diagram",
  [ContextSelectionSource.SEARCH]: "Search",
  [ContextSelectionSource.PROBLEMS]: "Errors & Warnings",
  [ContextSelectionSource.TOOLBOX]: "Toolbox",
  [ContextSelectionSource.RIBBON]: "Ribbon",
  [ContextSelectionSource.PROPERTY_GRID]: "Properties",
};

function sourceLabel(source: number): string {
  return SOURCE_LABELS[source] ?? "Unknown";
}

function kindLabel(detail: ContextLevelDetail | undefined): string {
  if (detail?.detail.case !== "entry") {
    return "—";
  }
  switch (detail.detail.value.kind) {
    case EntryKind.FOLDER:
      return "Folder";
    case EntryKind.FILE:
      return "File";
    default:
      return "—";
  }
}

/** A chain flattened outermost first, paired with the backend's detail for each level. */
export function levelsOf(selection: ContextSelection, details: ContextLevelDetail[]): Array<{ level: ContextSelection; detail: ContextLevelDetail | undefined }> {
  const levels: Array<{ level: ContextSelection; detail: ContextLevelDetail | undefined }> = [];
  let cursor: ContextSelection | undefined = selection;
  let index = 0;
  while (cursor) {
    levels.push({ level: cursor, detail: details[index] });
    cursor = cursor.detail.case === "child" ? cursor.detail.value : undefined;
    index++;
  }
  return levels;
}

/**
 * Shows what is currently selected, straight off the context stream - a pure subscriber
 * that makes no call of its own. Display only: editing a property is the business of a
 * later spec, as is anything diagram-element-specific.
 */
export function PropertyGridPanel() {
  const { selection, levels } = useContextSelection();

  if (!selection) {
    return (
      // TODO(adp-diagram-ide): property *editing* replaces this read-only view; the
      // "nothing selected" state itself stays.
      <div className="panel-placeholder" data-future-spec="adp-diagram-ide">
        <p className="panel-placeholder-title">Nothing selected</p>
        <p className="panel-placeholder-description">Select a file or folder in the explorer to see its properties here.</p>
      </div>
    );
  }

  return (
    <div className="property-grid" data-future-spec="adp-diagram-ide">
      {levelsOf(selection, levels).map(({ level, detail }, index) => {
        const segments = level.path?.segments ?? [];
        const name = segments[segments.length - 1] ?? "";
        const available = detail?.detail.case === "entry" ? detail.detail.value.available : true;
        return (
          <section className="property-grid-level" key={index}>
            <h3 className="property-grid-level-title">{name}</h3>
            <dl className="property-grid-rows">
              <div className="property-grid-row">
                <dt>Path</dt>
                <dd>{segments.join("/")}</dd>
              </div>
              <div className="property-grid-row">
                <dt>Kind</dt>
                <dd>{kindLabel(detail)}</dd>
              </div>
              <div className="property-grid-row">
                <dt>Available</dt>
                <dd>{available ? "Yes" : "No"}</dd>
              </div>
              <div className="property-grid-row">
                <dt>Selected in</dt>
                <dd>{sourceLabel(level.source)}</dd>
              </div>
            </dl>
          </section>
        );
      })}
    </div>
  );
}
