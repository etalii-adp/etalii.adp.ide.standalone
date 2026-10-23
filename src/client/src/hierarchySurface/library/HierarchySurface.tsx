import { type KeyboardEvent, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import "@client/table/library/table.css";
import "./hierarchySurface.css";
import type { HierarchySurfaceModel, HierarchySurfaceNode } from "./api/hierarchySurfaceModel";
import { ConflictBar } from "./ConflictBar";
import type { HierarchySurfaceAdd, HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";
import { AddControl, addedNode } from "./editing/AddControl";
import { ConfirmDelete, deleteCount } from "./editing/ConfirmDelete";
import type { EntryEditing, EntryTextAttribute, OpenField } from "./editing/entryEditing";
import { EntryTextField } from "./editing/EntryTextField";
import { type DragEntry, type EntryDrop, useDragEntry } from "./editing/useDragEntry";
import { FindingList } from "./FindingList";
import { HierarchySurfaceBox } from "./HierarchySurfaceBox";
import { HierarchySurfaceTrail } from "./HierarchySurfaceTrail";
import { ancestorsOf, descendantsOf, type HierarchyIndex, indexOf, levelsBelow, levelsThatFit } from "./layout";
import { NoteList } from "./notes/NoteList";
import { noteMarkOf } from "./notes/noteMark";
import { revealOf } from "./references/reveal";

/** A key with the modifiers held, as the backend matches it against an action's shortcut. */
export interface HierarchySurfaceChord {
  key: string;
  ctrl: boolean;
  shift: boolean;
  alt: boolean;
  meta: boolean;
}

export interface HierarchySurfaceProps {
  model: HierarchySurfaceModel;
  definition: HierarchySurfaceDefinition;
  /**
   * Sends one gesture: an operation of the specification, the node it is about, and its parameters. Resolves to the
   * refusal as a sentence, or `""` when it was accepted; anything else is taken as accepted.
   */
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
  /** Sends a key pressed on a selected node to the backend's actions; resolves to the refusal, or `""`. */
  onShortcut?: (nodeId: string, chord: HierarchySurfaceChord) => Promise<string>;
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
 * A node is added by the control at the end of every level and of the page, or by a key on a selected node, with no
 * dialog: the page opens the new node's title for typing (FR-012). A node is dragged by its grip, with everything in
 * it (FR-013), and deleted with Delete, asking first when anything concerns it (FR-014). The icon at a box's top right
 * opens the list of its notes, one box's at a time, where they are added, changed, resolved and removed (FR-015, FR-016).
 * `#` in a description refers to another node, which stands there as a box; selecting the box shows its target, changing
 * focus and expanding what hides it, and writes nothing but the collapse the registration keeps (FR-017, FR-018). A
 * label's values are picked, created and taken off in a field below the title, and its list is edited from there (FR-011).
 *
 * It knows no designer type: which type is a level, which an entry, which labels they carry, which icon a note shows
 * and which operation each gesture sends are the definition's.
 */
export function HierarchySurface({ model, definition, onGesture, onShortcut, selectedNode, onSelectNode, pendingEdits = 0 }: HierarchySurfaceProps) {
  const index = useMemo(() => indexOf(model), [model]);
  const [focus, setFocus] = useState("");
  const [selected, setSelected] = useState(selectedNode ?? "");
  // Collapsing an entry with no number of its own is the page's alone: the document has nothing to keep it by.
  const [localCollapse, setLocalCollapse] = useState<ReadonlySet<string>>(new Set());
  const [width, setWidth] = useState(0);
  const [field, setField] = useState<OpenField | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState<{ nodeId: string; question: string } | null>(null);
  // The node whose list of notes is open; one at a time.
  const [openNotes, setOpenNotes] = useState("");
  const pageRef = useRef<HTMLDivElement>(null);
  const fieldRef = useRef(field);
  fieldRef.current = field;
  // The page as it was when an add was asked for, until the node it adds arrives; then its title opens for typing.
  const awaitingAdd = useRef<readonly HierarchySurfaceNode[] | null>(null);
  const pendingRef = useRef(pendingEdits);
  pendingRef.current = pendingEdits;
  // The node a reference box asked to show, until its box is on the page to scroll to.
  const revealing = useRef<string | null>(null);
  // What a reference can name: a node without a number of its own cannot be written as one.
  const candidates = useMemo(() => model.nodes.filter((node) => !isLineId(node.id)), [model.nodes]);

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
            references: candidates,
          },
    [readOnly, field, commitField, closeField, candidates],
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

  /** Shows the node a reference box names: the focus that keeps it in sight, every level above it expanded, selected. */
  const reveal = useCallback(
    (targetId: string) => {
      const plan = revealOf(index, targetId, focused, fit, isCollapsed);
      if (plan === null) {
        return;
      }
      setFocus(plan.focus);
      for (const level of plan.expand) {
        toggle(level);
      }
      select(targetId);
      revealing.current = targetId;
    },
    [index, focused, fit, isCollapsed, toggle, select],
  );

  // Once the revealed node's box is on the page, which can take the model an expand brings, it is scrolled to.
  useEffect(() => {
    const id = revealing.current;
    const box = id === null ? null : pageRef.current?.querySelector(`.hs-box[data-node-id="${id}"]`);
    if (box !== null && box !== undefined) {
      revealing.current = null;
      box.scrollIntoView?.({ block: "nearest" });
    }
  });

  // The node an add asked for has arrived: it is selected and its title is open for typing.
  useEffect(() => {
    const before = awaitingAdd.current;
    if (before === null || before === model.nodes) {
      return;
    }
    const added = addedNode(before, model.nodes);
    if (added !== undefined) {
      awaitingAdd.current = null;
      select(added.id);
      setField({ nodeId: added.id, attribute: "title", text: added.title, label: `Title of ${added.number}` });
      return;
    }
    // An edit that added nothing, with nothing else on its way, ends the wait: the add was not written.
    if (model.cause === "edit" && pendingRef.current === 0) {
      awaitingAdd.current = null;
    }
  }, [model, select]);

  /** Sends an add, from a control or a key; a refusal ends the wait for its node at once. */
  const add = useCallback(
    (send: () => unknown) => {
      awaitingAdd.current = model.nodes;
      void Promise.resolve(send()).then((refusal) => {
        if (typeof refusal === "string" && refusal !== "") {
          awaitingAdd.current = null;
        }
      });
    },
    [model.nodes],
  );

  const addTo = useCallback(
    (levelId: string, what: HierarchySurfaceAdd) => add(() => onGesture(what.operation, levelId, {})),
    [add, onGesture],
  );

  const move = useCallback(
    (dragged: HierarchySurfaceNode, drop: EntryDrop) =>
      onGesture(dragged.role === "level" ? definition.operations.moveLevel : definition.operations.moveEntry, dragged.id, {
        [definition.moveParams.into]: drop.into,
        [definition.moveParams.after]: drop.after,
      }),
    [onGesture, definition.operations.moveLevel, definition.operations.moveEntry, definition.moveParams],
  );
  const drag = useDragEntry(index, move);

  /** Delete on a selected node: at once when nothing concerns it, otherwise after the definition's question. */
  const remove = useCallback(
    (node: HierarchySurfaceNode) => {
      const count = deleteCount(model, index, node.id);
      if (count < definition.deletion.threshold) {
        onGesture(definition.operations.delete, node.id, {});
        return;
      }
      setDeleting({ nodeId: node.id, question: definition.deletion.message(node.number, count) });
    },
    [model, index, definition.deletion, definition.operations.delete, onGesture],
  );

  const onKeyDown = (event: KeyboardEvent) => {
    // A field keeps its own keys, Enter and Delete among them.
    const typing = event.target instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName);
    const node = index.byId.get(selected);
    if (typing || readOnly || node === undefined) {
      return;
    }
    if (event.key === "Delete") {
      event.preventDefault();
      remove(node);
      return;
    }
    if (onShortcut !== undefined && (event.ctrlKey || event.metaKey) && definition.shortcutKeys.includes(event.key)) {
      event.preventDefault();
      const chord = { key: event.key, ctrl: true, shift: event.shiftKey, alt: event.altKey, meta: false };
      add(() => onShortcut(node.id, chord));
    }
  };

  const roots = focused === "" ? (index.children.get("") ?? []) : [index.byId.get(focused)!];
  const trail = focused === "" ? [] : [...ancestorsOf(index, focused), index.byId.get(focused)!];

  return (
    <div className="hs-surface" aria-busy={pendingEdits > 0} onKeyDown={onKeyDown}>
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
            references={field.attribute === "description" ? candidates : undefined}
          />
        </div>
      )}
      {readOnly && (
        <p className="hs-read-only" role="status">
          <span className="mdi mdi-lock-outline" aria-hidden="true" /> {model.readOnlyReason}
        </p>
      )}
      <FindingList findings={model.findings} onSelect={select} />
      <div className="hs-page" ref={pageRef} role="tree" aria-label={model.title} tabIndex={0}>
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
            onAdd={readOnly ? undefined : addTo}
            drag={readOnly ? undefined : drag}
            openNotes={openNotes}
            onToggleNotes={(id) => setOpenNotes((current) => (current === id ? "" : id))}
            onGesture={onGesture}
            readOnly={readOnly}
            onReveal={reveal}
          />
        ))}
        {!readOnly && focused === "" && <AddControl adds={[definition.operations.addTopLevel]} within="" onAdd={(what) => addTo("", what)} />}
      </div>
      <ConfirmDelete
        question={deleting?.question ?? ""}
        onConfirm={() => {
          if (deleting !== null) {
            onGesture(definition.operations.delete, deleting.nodeId, {});
          }
          setDeleting(null);
        }}
        onCancel={() => setDeleting(null)}
      />
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
  /** Adds into a level by one of its controls; absent when the document cannot be changed. */
  onAdd?: (levelId: string, what: HierarchySurfaceAdd) => void;
  /** Dragging a node by its grip; absent when the document cannot be changed. */
  drag?: DragEntry;
  /** The node whose list of notes is open, or empty. */
  openNotes: string;
  onToggleNotes: (id: string) => void;
  onGesture: HierarchySurfaceProps["onGesture"];
  readOnly: boolean;
  onReveal: (id: string) => void;
}

function NodeView(props: NodeViewProps) {
  const { node, depth, fit, index, model, definition, selected, isCollapsed, onToggle, onSelect, onFocus, editingOf, onAdd, drag, openNotes, onToggleNotes, onGesture, readOnly, onReveal } = props;
  const collapsed = isCollapsed(node);
  const level = node.role === "level";
  // A collapsed level's mark counts every note inside it, since nothing inside it shows.
  const about = useMemo(
    () => new Set([node.id, ...(level && collapsed ? descendantsOf(index, node.id).map((inner) => inner.id) : [])]),
    [index, node.id, level, collapsed],
  );
  const notes = noteMarkOf(model.notes, about, definition);
  const notesOpen = openNotes === node.id;
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
      notesOpen={notesOpen}
      onToggleNotes={() => onToggleNotes(node.id)}
      noteList={
        notesOpen && (
          // The list shows what the mark counts, and the resolved notes beside them.
          <NoteList
            notes={model.notes.filter((note) => about.has(note.aboutId))}
            number={node.number}
            definition={definition}
            canEdit={!readOnly}
            onGesture={onGesture}
            nodeId={node.id}
            onClose={() => onToggleNotes(node.id)}
          />
        )
      }
      hiddenLevels={hiddenLevels}
      onToggle={() => onToggle(node)}
      onSelect={() => onSelect(node.id)}
      onFocus={level ? () => onFocus(node.id) : undefined}
      editing={editingOf(node)}
      grip={drag?.grip(node)}
      dropHint={drag?.hint?.nodeId === node.id ? drag.hint.kind : undefined}
      nodeOf={(id) => index.byId.get(id)}
      onReveal={onReveal}
      nodes={model.nodes}
      onLabelGesture={readOnly ? undefined : onGesture}
    />
  );

  if (!level) {
    return <div className="hs-entry">{box}</div>;
  }

  const content = collapsed ? [] : [...entries, ...(showsSubLevels ? subLevels : [])];
  const adds = onAdd === undefined || collapsed ? [] : [definition.operations.addEntry, definition.operations.addLevel];
  return (
    <div className="hs-level">
      <div className="hs-level-box">{box}</div>
      {content.length + adds.length > 0 && (
        <div className="hs-level-content" role="group">
          {content.map((child) => (
            <NodeView key={child.id} {...props} node={child} depth={child.role === "level" ? depth + 1 : depth} />
          ))}
          {adds.length > 0 && <AddControl adds={adds} within={node.number} onAdd={(what) => onAdd?.(node.id, what)} />}
        </div>
      )}
    </div>
  );
}
