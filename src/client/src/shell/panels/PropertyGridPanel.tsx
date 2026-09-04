import { useCallback, useEffect, useRef, useState } from "react";
import { EntryKind } from "../../generated/hierarchy_pb";
import { ContextSelectionSource, type ContextLevelDetail, type ContextProperty, type ContextSelection } from "../../generated/context_pb";
import { useContextConnection, useContextSelection } from "../context/ContextConnectionProvider";
import { PropertyRow } from "./PropertyRow";

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
  if (detail?.detail.case === "element") {
    return "Node";
  }
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

/** Ungrouped rows first, then each group in the order its first property arrived. */
export function groupsOf(properties: ContextProperty[]): Array<{ name: string; properties: ContextProperty[] }> {
  const groups: Array<{ name: string; properties: ContextProperty[] }> = [];
  for (const property of properties) {
    const existing = groups.find((group) => group.name === property.group);
    if (existing) {
      existing.properties.push(property);
    } else {
      groups.push({ name: property.group, properties: [property] });
    }
  }
  return groups.sort((left, right) => (left.name === "" ? -1 : right.name === "" ? 1 : 0));
}

/**
 * What is selected, and what can be changed about it.
 *
 * The properties come from whichever module owns the selection, described as data: a label, a
 * value, an editor and — when it cannot be edited — the reason. Nothing here knows what a
 * technology or a note is, exactly as the context menu knows no action and the toolbox knows
 * no entry.
 *
 * Every edit goes to the backend as a command on the project history, so a value changed here
 * is one undo away, and changing it from the grid or from the "Rename…" dialog is the same
 * edit. Values are written on Enter or on losing focus and never while typing — see
 * {@link PropertyRow}, which is where that rule lives.
 */
export function PropertyGridPanel() {
  const { selection, levels } = useContextSelection();
  const { describeProperties, setProperty } = useContextConnection();
  const [properties, setProperties] = useState<ContextProperty[]>([]);
  // Kept apart from an empty `properties`, which is the ordinary "nothing to edit here" state.
  const [describeError, setDescribeError] = useState("");

  // Held in a ref rather than depended on. What should re-ask for properties is the selection
  // changing, not the identity of the function that asks - and a caller that hands us a fresh
  // closure each render would otherwise drive this effect in a loop, which is a poor way for a
  // panel to react to somebody else's memoisation.
  const describeRef = useRef(describeProperties);
  describeRef.current = describeProperties;

  // Re-asked whenever the selection changes *or* the pushed detail does. The second half is
  // what makes an edit show up: committing a value changes the document, the document change
  // is pushed, and the properties are read again - so the grid never shows its own optimistic
  // guess about what the backend did.
  const pushedDetail = levels.map((detail) => detail.detail.case + ":" + JSON.stringify(detail.detail.value ?? null)).join("|");
  useEffect(() => {
    let cancelled = false;
    if (!selection) {
      setProperties([]);
      setDescribeError("");
      return;
    }

    void describeRef.current().then((described) => {
      if (!cancelled) {
        setProperties(described.properties);
        setDescribeError(described.error);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [selection, pushedDetail]);

  const setRef = useRef(setProperty);
  setRef.current = setProperty;

  const commit = useCallback(
    async (propertyId: string, value: string): Promise<string> => {
      const outcome = await setRef.current(propertyId, value);
      if (outcome.accepted) {
        // Nothing to set here: the change lands on the document, the document change is
        // pushed, and the effect above reads the properties again.
        return "";
      }
      return outcome.error.length > 0 ? outcome.error : "That value was not accepted.";
    },
    [],
  );

  if (!selection) {
    return (
      <div className="panel-placeholder">
        <p className="panel-placeholder-title">Nothing selected</p>
        <p className="panel-placeholder-description">Select a file or folder in the explorer to see its properties here.</p>
      </div>
    );
  }

  const chain = levelsOf(selection, levels);
  const innermostLevel = chain[chain.length - 1];

  return (
    <div className="property-grid">
      {chain.map(({ level, detail }, index) => {
        const segments = level.path?.segments ?? [];
        const element = detail?.detail.case === "element" ? detail.detail.value : undefined;
        const name = element?.text ?? segments[segments.length - 1] ?? "";
        const available = detail?.detail.case === "entry" ? detail.detail.value.available : true;
        const isInnermost = level === innermostLevel?.level;

        return (
          <section className="property-grid-level" key={index}>
            <h3 className="property-grid-level-title">{name}</h3>
            <dl className="property-grid-rows">
              {/* Only the innermost level is editable. The outer levels are the path that led
                  here - a folder above a diagram is context, not a second thing to edit, and
                  offering two editable Names at once would be a good way to change the wrong
                  one. */}
              {/* A describe that failed says so, rather than showing the empty grid a selection
                  with no properties shows. The two look identical from an array alone, and a
                  user told nothing would reasonably conclude this thing has no properties. */}
              {isInnermost && describeError.length > 0 && (
                <div className="property-grid-row property-grid-row-readonly property-grid-row-unavailable">
                  <dt>Properties</dt>
                  <dd>
                    <span className="property-grid-value">Unavailable</span>
                    <span className="property-grid-readonly-reason">{describeError}</span>
                  </dd>
                </div>
              )}

              {isInnermost &&
                groupsOf(properties).map((group) => (
                  <div className="property-grid-group" key={group.name}>
                    {group.name.length > 0 && <h4 className="property-grid-group-title">{group.name}</h4>}
                    {group.properties.map((property) => (
                      <PropertyRow
                        key={property.id}
                        property={property}
                        onCommit={(value) => commit(property.id, value)}
                      />
                    ))}
                  </div>
                ))}

              {element === undefined && (
                <div className="property-grid-row">
                  <dt>Path</dt>
                  <dd>{segments.join("/")}</dd>
                </div>
              )}
              <div className="property-grid-row">
                <dt>Kind</dt>
                <dd>{kindLabel(detail)}</dd>
              </div>
              {element === undefined && (
                <div className="property-grid-row">
                  <dt>Available</dt>
                  <dd>{available ? "Yes" : "No"}</dd>
                </div>
              )}
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
