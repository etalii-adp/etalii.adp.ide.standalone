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
import { DROP_SLOT, type DragEntry, type DropHint, type EntryDrop, slotIndex, useDragEntry } from "./editing/useDragEntry";
import { FindingList } from "./FindingList";
import { HierarchySurfaceBox } from "./HierarchySurfaceBox";
import { HierarchySurfaceTrail } from "./HierarchySurfaceTrail";
import { ancestorsOf, descendantsOf, type HierarchyIndex, indexOf, levelsBelow, levelsThatFit } from "./layout";
import { ColumnEdge } from "./columns/ColumnEdge";
import { type ColumnWidths, useColumnWidths } from "./columns/useColumnWidths";
import { FilterBar } from "./narrowing/FilterBar";
import { type Narrowing, narrowingOf, parseQuery, queryText, sortOf, unhiding, withSort } from "./narrowing/filterQuery";
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
 * label's values are picked, created and taken off in a field below the title, and its list is edited from there (FR-011);
 * a label of one value is picked from its list, edited from there too. The bar above the page narrows it by a query
 * and sorts the entries within each level, writing nothing, and stores the query under a name (FR-019, FR-020, FR-048).
 * A finding stays listed whatever the query hides, and selecting it, as a reference box, shows its node, taking out of
 * the query the words that hide it (FR-021).
 *
 * It knows no designer type: which type is a level, which an entry, which labels they carry, which icon a note shows
 * and which operation each gesture sends are the definition's.
 */
export function HierarchySurface({ model, definition, onGesture, onShortcut, selectedNode, onSelectNode, pendingEdits = 0 }: HierarchySurfaceProps) {
  const index = useMemo(() => indexOf(model), [model]);
  // The page's left edge, and whether it shows what is inside that level rather than the level itself.
  const [focus, setFocusAt] = useState({ id: "", inside: false });
  const setFocus = useCallback((id: string) => setFocusAt({ id, inside: false }), []);
  const [selected, setSelected] = useState(selectedNode ?? "");
  // Collapsing an entry with no number of its own is the page's alone: the document has nothing to keep it by.
  const [localCollapse, setLocalCollapse] = useState<ReadonlySet<string>>(new Set());
  const [width, setWidth] = useState(0);
  const [field, setField] = useState<OpenField | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState<{ nodeId: string; question: string } | null>(null);
  // The node whose list of notes is open; one at a time.
  const [openNotes, setOpenNotes] = useState("");
  // The query the bar applies, as typed; it is the page's own, never sent until it is stored.
  const [query, setQuery] = useState("");
  const words = useMemo(() => parseQuery(query, definition.labels), [query, definition.labels]);
  const narrowing = useMemo(() => narrowingOf(model, index, words), [model, index, words]);
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
  const focused = focus.id !== "" && index.byId.has(focus.id) ? focus.id : "";

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

  // The depth of the page's left column, 1 for the top levels: a column's width is kept by its depth.
  const rootLevel = focused === "" ? 1 : ancestorsOf(index, focused).length + (focus.inside ? 2 : 1);
  const columns = useColumnWidths(model.widths, definition.widths, onGesture);
  const fit = levelsThatFit(width, (column) => columns.widthOf(rootLevel + column));
  const readOnly = model.readOnlyReason !== "";
  // Where only one level fits, a level made the left edge shows its sub-levels in its place: beside it they cannot fit.
  const inside = focused !== "" && focus.inside;

  /** The mark of a level's hidden levels shows them: the level becomes the left edge, or its sub-levels do. */
  const showHidden = useCallback((id: string) => setFocusAt({ id, inside: fit < 2 }), [fit]);

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

  /**
   * Shows the node a reference box or a finding names: the query without the words that hide it, the focus that keeps
   * it in sight, every level above it expanded, selected.
   */
  const reveal = useCallback(
    (targetId: string) => {
      const plan = revealOf(index, targetId, focused, fit, isCollapsed);
      if (plan === null) {
        return;
      }
      const unhidden = unhiding(model, index, words, targetId);
      if (unhidden.length !== words.length) {
        setQuery(queryText(unhidden));
      }
      setFocus(plan.focus);
      for (const level of plan.expand) {
        toggle(level);
      }
      select(targetId);
      revealing.current = targetId;
    },
    [model, index, words, focused, fit, isCollapsed, toggle, select],
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

  // The node an add asked for has arrived: it is shown, where the page's width hides it too, selected, and its title is
  // open for typing.
  useEffect(() => {
    const before = awaitingAdd.current;
    if (before === null || before === model.nodes) {
      return;
    }
    const added = addedNode(before, model.nodes);
    if (added !== undefined) {
      awaitingAdd.current = null;
      reveal(added.id);
      setField({ nodeId: added.id, attribute: "title", text: added.title, label: `Title of ${added.number}` });
      return;
    }
    // An edit that added nothing, with nothing else on its way, ends the wait: the add was not written.
    if (model.cause === "edit" && pendingRef.current === 0) {
      awaitingAdd.current = null;
    }
  }, [model, reveal]);

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

  /**
   * Delete on a selected node: by the key, at once when nothing concerns it, otherwise after the definition's question;
   * by the control on its box, always after a question that says what it takes.
   */
  const remove = useCallback(
    (node: HierarchySurfaceNode, ask = false) => {
      const count = deleteCount(model, index, node.id);
      if (count < definition.deletion.threshold) {
        if (!ask) {
          onGesture(definition.operations.delete, node.id, {});
          return;
        }
        setDeleting({ nodeId: node.id, question: `Delete ${node.number} ${node.title}? Nothing else concerns it.` });
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

  const roots = (
    focused === ""
      ? (index.children.get("") ?? [])
      : inside
        ? (index.children.get(focused) ?? []).filter((node) => node.role === "level")
        : [index.byId.get(focused)!]
  ).filter(narrowing.shows);
  const entries = model.nodes.filter((node) => node.role === "entry");
  const filters = definition.filters;
  const sortKeys = useMemo(
    () => [
      ...(filters?.sortKeys ?? []),
      ...definition.labels.filter((label) => !label.many).map((label) => ({ key: label.label, label: label.pick?.words.value ?? label.label })),
    ],
    [filters, definition.labels],
  );
  // The trail follows the selection, and the focus while nothing is selected: the levels down to the one it stands in.
  const anchor = index.byId.get(selected) ?? index.byId.get(focused);
  const trail = anchor === undefined ? [] : anchor.role === "level" ? [...ancestorsOf(index, anchor.id), anchor] : ancestorsOf(index, anchor.id);

  return (
    <div className="hs-surface" aria-busy={pendingEdits > 0} onKeyDown={onKeyDown}>
      <HierarchySurfaceTrail title={model.title} path={trail} focused={inside ? null : focused} onFocus={setFocus} />
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
      {filters !== undefined && (
        <FilterBar
          text={query}
          onChange={setQuery}
          sort={sortOf(words)}
          onSort={(key) => setQuery(queryText(withSort(words, key)))}
          sortKeys={sortKeys}
          stored={model.filters}
          filters={filters}
          shown={entries.filter(narrowing.shows).length}
          total={entries.length}
          canEdit={!readOnly}
          onGesture={onGesture}
          labels={definition.labels}
          optionLists={model.optionLists}
        />
      )}
      <FindingList findings={model.findings} onSelect={reveal} />
      <div className="hs-page" ref={pageRef} role="tree" aria-label={model.title} tabIndex={0}>
        {withSlot(roots, focused === "" ? drag.hint : null, "").map((node) =>
          node.id === SLOT_ID ? (
            <DropSlot key={SLOT_ID} hint={drag.hint!} />
          ) : (
            <NodeView
              key={node.id}
              node={node}
              depth={0}
              rootLevel={rootLevel}
              columns={columns}
              resizable={!readOnly && definition.widths !== undefined}
              fit={fit}
              index={index}
              model={model}
              definition={definition}
              selected={selected}
              isCollapsed={isCollapsed}
              onToggle={toggle}
              onSelect={select}
              onFocus={setFocus}
              onShowHidden={showHidden}
              onDelete={readOnly ? undefined : (target) => remove(target, true)}
              editingOf={editingOf}
              onAdd={readOnly ? undefined : addTo}
              drag={readOnly ? undefined : drag}
              openNotes={openNotes}
              onToggleNotes={(id) => setOpenNotes((current) => (current === id ? "" : id))}
              onGesture={onGesture}
              readOnly={readOnly}
              onReveal={reveal}
              narrowing={narrowing}
            />
          ),
        )}
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
  /** The depth of the page's left column, 1 for the top levels. */
  rootLevel: number;
  /** The widths of the columns of levels, by depth. */
  columns: ColumnWidths;
  /** Whether a column's right edge is dragged to change its width. */
  resizable: boolean;
  fit: number;
  index: HierarchyIndex;
  model: HierarchySurfaceModel;
  definition: HierarchySurfaceDefinition;
  selected: string;
  isCollapsed: (node: HierarchySurfaceNode) => boolean;
  onToggle: (node: HierarchySurfaceNode) => void;
  onSelect: (id: string) => void;
  onFocus: (id: string) => void;
  onShowHidden: (id: string) => void;
  /** Asks to delete a node by the control on its box; absent when the document cannot be changed. */
  onDelete?: (node: HierarchySurfaceNode) => void;
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
  /** What the bar's query leaves of the page, and the order of a level's entries. */
  narrowing: Narrowing;
}

function NodeView(props: NodeViewProps) {
  const { node, depth, rootLevel, columns, resizable, fit, index, model, definition, selected, isCollapsed, onToggle, onSelect, onFocus, onShowHidden, onDelete, editingOf, onAdd, drag, openNotes, onToggleNotes, onGesture, readOnly, onReveal, narrowing } = props;
  const collapsed = isCollapsed(node);
  const level = node.role === "level";
  // A collapsed level's mark counts every note inside it, since nothing inside it shows.
  const about = useMemo(
    () => new Set([node.id, ...(level && collapsed ? descendantsOf(index, node.id).map((inner) => inner.id) : [])]),
    [index, node.id, level, collapsed],
  );
  const notes = noteMarkOf(model.notes, about, definition);
  const notesOpen = openNotes === node.id;
  const children = (index.children.get(node.id) ?? []).filter(narrowing.shows);
  const entries = narrowing.order(children.filter((child) => child.role !== "level"));
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
      onShowHidden={level ? () => onShowHidden(node.id) : undefined}
      onDelete={onDelete === undefined || isLineId(node.id) ? undefined : () => onDelete(node)}
      editing={editingOf(node)}
      grip={drag?.grip(node)}
      dropHint={drag?.hint?.nodeId === node.id ? drag.hint.kind : undefined}
      nodeOf={(id) => index.byId.get(id)}
      onReveal={onReveal}
      nodes={model.nodes}
      onLabelGesture={readOnly ? undefined : onGesture}
    />
  );

  const dragging = drag?.hint?.dragged.id === node.id ? " hs-dragging" : "";
  if (!level) {
    return <div className={`hs-entry${dragging}`}>{box}</div>;
  }

  const content = withSlot(collapsed ? [] : [...entries, ...(showsSubLevels ? subLevels : [])], drag?.hint ?? null, node.id);
  const adds = onAdd === undefined || collapsed ? [] : [definition.operations.addEntry, definition.operations.addLevel];
  return (
    <div className={`hs-level${dragging}`}>
      <div className="hs-level-box" style={{ flex: `0 0 ${columns.widthOf(rootLevel + depth)}px`, width: columns.widthOf(rootLevel + depth) }}>
        {box}
        {resizable && <ColumnEdge level={rootLevel + depth} columns={columns} />}
      </div>
      {content.length + adds.length > 0 && (
        <div className="hs-level-content" role="group">
          {content.map((child) =>
            child.id === SLOT_ID ? (
              <DropSlot key={SLOT_ID} hint={drag!.hint!} />
            ) : (
              <NodeView key={child.id} {...props} node={child} depth={child.role === "level" ? depth + 1 : depth} />
            ),
          )}
          {adds.length > 0 && <AddControl adds={adds} within={node.number} onAdd={(what) => onAdd?.(node.id, what)} />}
        </div>
      )}
    </div>
  );
}

/** The id that stands in a level's children for the place a dragged node would be dropped at. */
const SLOT_ID = "\u0000slot";

/** `shown` with the place a drop into `levelId` shows, where the drop would put the dragged node among them. */
function withSlot(shown: readonly HierarchySurfaceNode[], hint: DropHint | null, levelId: string): readonly HierarchySurfaceNode[] {
  if (hint === null || hint.drop.into !== levelId) {
    return shown;
  }
  const at = slotIndex(shown, hint);
  return at < 0 ? shown : [...shown.slice(0, at), { ...hint.dragged, id: SLOT_ID }, ...shown.slice(at)];
}

/** The place a dragged node would stand once dropped: its number and title, where the drop puts it. */
function DropSlot({ hint }: { hint: DropHint }) {
  return (
    <div className={`${DROP_SLOT} hs-drop-slot-${hint.dragged.role}`} aria-hidden="true">
      <span className="hs-number">{hint.dragged.number}</span> <span className="hs-drop-slot-title">{hint.dragged.title}</span>
    </div>
  );
}
