import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import "@client/table/library/table.css";
import "./hierarchySurface.css";
import type { HierarchySurfaceModel, HierarchySurfaceNode } from "./api/hierarchySurfaceModel";
import { ConflictBar } from "./ConflictBar";
import type { HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";
import type { EntryEditing, EntryTextAttribute, OpenField } from "./editing/entryEditing";
import { EntryTextField } from "./editing/EntryTextField";
import { FindingList } from "./FindingList";
import { HierarchySurfaceBox } from "./HierarchySurfaceBox";
import { HierarchySurfaceTrail } from "./HierarchySurfaceTrail";
import { ancestorsOf, descendantsOf, type HierarchyIndex, indexOf, levelsBelow, levelsThatFit } from "./layout";
import { noteMarkOf } from "./notes";

export interface HierarchySurfaceProps {
  model: HierarchySurfaceModel;
  definition: HierarchySurfaceDefinition;
  /** Sends one gesture: an operation of the specification, the node it is about, and its parameters. */
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
  selectedNode?: string;
  onSelectNode?: (id: string) => void;
  pendingEdits?: number;
}

/** Whether a node's id is its number, which a gesture can name; `line/<n>` ids last only as long as one model. */
const isLineId = (id: string) => id.startsWith("line/");

/**
 * A designer document of the hierarchy kind as one page that scrolls from top to bottom, with no tree beside it
 * (DESL section 7.7, FR-006): each item a box with its requirements and then its sub-items to its right, as high as
 * what stands to its right, neighbouring boxes sharing their borders (FR-007). As many levels as fit the page's width
 * are shown; an item whose deeper levels do not fit carries a mark, and any item can be made the left edge of the page,
 * with a trail leading back (FR-008). Focus is this component's own state and is never sent.
 *
 * A title or description is typed in place; that text is the one thing on the page that can be unsaved (R11). When
 * another program changes the file while a field is open, the page is shown as the file now is around it, and a bar
 * offers to drop the text or keep it; kept, committing it is a new edit of the file as it is now (FR-023).
 *
 * It knows no designer type: which type is a level, which an entry, which labels they carry and which icon a note
 * shows are the definition's.
 */
export function HierarchySurface({ model, definition, onGesture, selectedNode, onSelectNode, pendingEdits = 0 }: HierarchySurfaceProps) {
  const index = useMemo(() => indexOf(model), [model]);
  const [focus, setFocus] = useState("");
  const [selected, setSelected] = useState(selectedNode ?? "");
  // Collapsing an entry with no number of its own is the page's alone: the document has nothing to keep it by.
  const [localCollapse, setLocalCollapse] = useState<ReadonlySet<string>>(new Set());
  const [width, setWidth] = useState(0);
  const [field, setField] = useState<OpenField | null>(null);
  const [conflict, setConflict] = useState(false);
  const pageRef = useRef<HTMLDivElement>(null);
  const fieldRef = useRef(field);
  fieldRef.current = field;

  useEffect(() => setSelected(selectedNode ?? ""), [selectedNode]);

  // A focus on a node the file no longer has falls back to the whole page.
  const focused = focus !== "" && index.byId.has(focus) ? focus : "";

  useLayoutEffect(() => {
    const page = pageRef.current;
    if (page === null) {
      return;
    }
    setWidth(page.clientWidth);
    if (typeof ResizeObserver === "undefined") {
      return;
    }
    const observer = new ResizeObserver(() => setWidth(page.clientWidth));
    observer.observe(page);
    return () => observer.disconnect();
  }, []);

  const fit = levelsThatFit(width);
  const readOnly = model.readOnlyReason !== "";

  // A change from outside while a field is open asks what to do with the text; the page itself is already the file's.
  useEffect(() => {
    if (model.cause === "outside" && fieldRef.current !== null) {
      setConflict(true);
    }
  }, [model]);

  const closeField = useCallback(() => {
    setField(null);
    setConflict(false);
  }, []);

  const commitField = useCallback(() => {
    const open = fieldRef.current;
    if (open === null) {
      return;
    }
    closeField();
    const node = index.byId.get(open.nodeId);
    // Unchanged is no edit. A node the file no longer has is still sent: refusing it is the file's to say.
    if (node !== undefined && node[open.attribute] === open.text) {
      return;
    }
    const operation = open.attribute === "title" ? definition.operations.setTitle : definition.operations.setDescription;
    onGesture(operation, open.nodeId, { [open.attribute]: open.text });
  }, [closeField, index, definition.operations, onGesture]);

  const editingOf = useCallback(
    (node: HierarchySurfaceNode): EntryEditing | undefined =>
      readOnly
        ? undefined
        : {
            field: field !== null && field.nodeId === node.id ? field : null,
            open: (attribute: EntryTextAttribute) =>
              setField({ nodeId: node.id, attribute, text: node[attribute], label: `${attribute === "title" ? "Title" : "Description"} of ${node.number}` }),
            change: (text: string) => setField((current) => (current === null ? null : { ...current, text })),
            commit: commitField,
            cancel: closeField,
          },
    [readOnly, field, commitField, closeField],
  );

  const isCollapsed = useCallback(
    (node: HierarchySurfaceNode) => (localCollapse.has(node.id) ? !node.collapsed : node.collapsed),
    [localCollapse],
  );

  const toggle = useCallback(
    (node: HierarchySurfaceNode) => {
      if (isLineId(node.id) || readOnly) {
        setLocalCollapse((current) => {
          const next = new Set(current);
          if (next.has(node.id)) {
            next.delete(node.id);
          } else {
            next.add(node.id);
          }
          return next;
        });
        return;
      }
      onGesture(definition.operations.toggleCollapsed, node.id, {});
    },
    [onGesture, definition.operations.toggleCollapsed, readOnly],
  );

  const select = useCallback(
    (id: string) => {
      setSelected(id);
      onSelectNode?.(id);
    },
    [onSelectNode],
  );

  const roots = focused === "" ? (index.children.get("") ?? []) : [index.byId.get(focused)!];
  const trail = focused === "" ? [] : [...ancestorsOf(index, focused), index.byId.get(focused)!];

  return (
    <div className="hs-surface" aria-busy={pendingEdits > 0}>
      {focused !== "" && <HierarchySurfaceTrail title={model.title} path={trail} onFocus={setFocus} />}
      {conflict && field !== null && <ConflictBar onShowFile={closeField} onKeepText={() => setConflict(false)} />}
      {field !== null && !index.byId.has(field.nodeId) && (
        // The file no longer has the node the field is open on, so its box is gone; the text is kept here instead,
        // and committing it is for the file to refuse (R10).
        <div className="hs-orphan-field">
          <EntryTextField
            text={field.text}
            multiline={field.attribute === "description"}
            label={field.label}
            onChange={(text) => setField((current) => (current === null ? null : { ...current, text }))}
            onCommit={commitField}
            onCancel={closeField}
          />
        </div>
      )}
      {readOnly && (
        <p className="hs-read-only" role="status">
          <span className="mdi mdi-lock-outline" aria-hidden="true" /> {model.readOnlyReason}
        </p>
      )}
      <FindingList findings={model.findings} onSelect={select} />
      <div className="hs-page" ref={pageRef} role="tree" aria-label={model.title}>
        {roots.map((node) => (
          <NodeView
            key={node.id}
            node={node}
            depth={0}
            fit={fit}
            index={index}
            model={model}
            definition={definition}
            selected={selected}
            isCollapsed={isCollapsed}
            onToggle={toggle}
            onSelect={select}
            onFocus={setFocus}
            editingOf={editingOf}
          />
        ))}
      </div>
    </div>
  );
}

interface NodeViewProps {
  node: HierarchySurfaceNode;
  /** The node's level counted from the page's left edge. */
  depth: number;
  fit: number;
  index: HierarchyIndex;
  model: HierarchySurfaceModel;
  definition: HierarchySurfaceDefinition;
  selected: string;
  isCollapsed: (node: HierarchySurfaceNode) => boolean;
  onToggle: (node: HierarchySurfaceNode) => void;
  onSelect: (id: string) => void;
  onFocus: (id: string) => void;
  editingOf: (node: HierarchySurfaceNode) => EntryEditing | undefined;
}

function NodeView(props: NodeViewProps) {
  const { node, depth, fit, index, model, definition, selected, isCollapsed, onToggle, onSelect, onFocus, editingOf } = props;
  const collapsed = isCollapsed(node);
  const level = node.role === "level";
  // A collapsed level's mark counts every note inside it, since nothing inside it shows.
  const about = useMemo(
    () => new Set([node.id, ...(level && collapsed ? descendantsOf(index, node.id).map((inner) => inner.id) : [])]),
    [index, node.id, level, collapsed],
  );
  const notes = noteMarkOf(model.notes, about, definition);
  const children = index.children.get(node.id) ?? [];
  const entries = children.filter((child) => child.role !== "level");
  const subLevels = children.filter((child) => child.role === "level");
  const showsSubLevels = depth + 1 < fit;
  const hiddenLevels = level && !showsSubLevels ? levelsBelow(index, node.id) : 0;

  const box = (
    <HierarchySurfaceBox
      node={node}
      definition={definition}
      optionLists={model.optionLists}
      collapsed={collapsed}
      selected={selected === node.id}
      notes={notes}
      hiddenLevels={hiddenLevels}
      onToggle={() => onToggle(node)}
      onSelect={() => onSelect(node.id)}
      onFocus={level ? () => onFocus(node.id) : undefined}
      editing={editingOf(node)}
    />
  );

  if (!level) {
    return <div className="hs-entry">{box}</div>;
  }

  const content = collapsed ? [] : [...entries, ...(showsSubLevels ? subLevels : [])];
  return (
    <div className="hs-level">
      <div className="hs-level-box">{box}</div>
      {content.length > 0 && (
        <div className="hs-level-content" role="group">
          {content.map((child) => (
            <NodeView key={child.id} {...props} node={child} depth={child.role === "level" ? depth + 1 : depth} />
          ))}
        </div>
      )}
    </div>
  );
}
