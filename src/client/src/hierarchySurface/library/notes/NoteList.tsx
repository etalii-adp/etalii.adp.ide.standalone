import { type KeyboardEvent, type ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { HierarchySurfaceNote } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "../definition/hierarchySurfaceDefinition";

export interface NoteListProps {
  /** The notes the list shows: those about the box's node, and with a collapsed level, about everything inside it. */
  notes: readonly HierarchySurfaceNote[];
  /** The node's number, which the list is titled by. */
  number: string;
  definition: HierarchySurfaceDefinition;
  /** Whether notes can be added, changed or removed: false while the document cannot be changed. */
  canEdit: boolean;
  /** Sends a gesture: the operation, the node (an add) or the note (the others), and its params. */
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
  /** The node an add is about. */
  nodeId: string;
  onClose: () => void;
}

interface Draft {
  kind: string;
  text: string;
  detail: string;
}

/** The icons of a note's controls; what each says is the definition's, as their tooltip. */
const RESOLVE_ICON = "mdi-check-circle-outline";
const REOPEN_ICON = "mdi-restore";
const CHANGE_ICON = "mdi-pencil-outline";
const REMOVE_ICON = "mdi-delete-outline";

/** The gap between the list and the icon it opened from, and between the list and the window's edges, in pixels. */
const GAP = 4;

/**
 * The list of a box's notes, opened from its icon (FR-016): each with its kind, the number it is about, its text and its
 * detail, resolved ones too; each changed, resolved or reopened, or removed by the icons beside it; and a new one added
 * at the bottom, its kind above its text and the add below them. A gesture is sent as soon as it is made; what the file
 * then says is the page's next model. Escape, wherever the focus is, or a click outside closes it.
 *
 * It is drawn at the document's top, above every pane, and placed under the box's icons, in the window: inside the page
 * it was cut off by the page's own edges, behind the panes beside it.
 */
export function NoteList({ notes, number, definition, canEdit, onGesture, nodeId, onClose }: NoteListProps) {
  const kinds = Object.keys(definition.noteIcons);
  const { operations, params, words } = definition.notes;
  const [changing, setChanging] = useState<{ noteId: string; draft: Draft } | null>(null);
  const [adding, setAdding] = useState<Draft>({ kind: kinds[0] ?? "", text: "", detail: "" });
  const ref = useRef<HTMLDivElement>(null);
  // Where the list stands in the page, which it is placed against.
  const spot = useRef<HTMLSpanElement>(null);
  const [place, setPlace] = useState<{ left: number; top: number } | null>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  // Escape drops a change being typed, and otherwise closes the list.
  const escapeRef = useRef(() => {});
  escapeRef.current = () => (changing !== null ? setChanging(null) : onClose());

  useEffect(() => {
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && ref.current !== null && !ref.current.contains(event.target) && !(event.target instanceof Element && event.target.closest(".hs-note-mark, .hs-note-add") !== null)) {
        closeRef.current();
      }
    };
    // Escape pressed outside the list counts too: a removed note, or a note opened for a change, takes the focus its
    // button held with it.
    const escape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        escapeRef.current();
      }
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, []);

  // Under the box's icons, its right edge on theirs, kept in the window; above them where the window has no room below.
  useLayoutEffect(() => {
    const measure = () => {
      const anchor = spot.current?.closest(".hs-box")?.querySelector(".hs-box-icons") ?? spot.current?.parentElement;
      const list = ref.current;
      if (anchor === null || anchor === undefined || list === null) {
        return;
      }
      const at = anchor.getBoundingClientRect();
      const { width, height } = list.getBoundingClientRect();
      const left = Math.max(GAP, Math.min(at.right - width, window.innerWidth - width - GAP));
      const below = at.bottom + GAP;
      const top = below + height > window.innerHeight - GAP && at.top - GAP - height >= GAP ? at.top - GAP - height : below;
      setPlace((current) => (current !== null && current.left === left && current.top === top ? current : { left, top }));
    };
    measure();
    window.addEventListener("resize", measure);
    // The page scrolls under it: it follows its box.
    document.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      document.removeEventListener("scroll", measure, true);
    };
  });

  // The list keeps its keys: Delete or Enter typed here is not the page's delete or add.
  const onKeyDown = (event: KeyboardEvent) => {
    event.stopPropagation();
    if (event.key === "Escape") {
      event.preventDefault();
      escapeRef.current();
    }
  };

  const add = () => {
    if (adding.text.trim() === "") {
      return;
    }
    onGesture(operations.add, nodeId, { [params.kind]: adding.kind, [params.text]: adding.text });
    setAdding({ ...adding, text: "" });
  };

  const save = () => {
    if (changing === null || changing.draft.text.trim() === "") {
      return;
    }
    const { noteId, draft } = changing;
    onGesture(operations.change, noteId, { [params.kind]: draft.kind, [params.text]: draft.text, [params.detail]: draft.detail });
    setChanging(null);
  };

  return (
    <span ref={spot} className="hs-note-spot">
      {createPortal(
        <div
          ref={ref}
          className="hs-note-list"
          role="dialog"
          aria-label={`${words.list} of ${number}`}
          style={place === null ? { visibility: "hidden" } : { left: place.left, top: place.top }}
          onKeyDown={onKeyDown}
          onClick={(event) => event.stopPropagation()}
          onDoubleClick={(event) => event.stopPropagation()}
          onPointerDown={(event) => event.stopPropagation()}
        >
          <ul className="hs-notes">
            {notes.map((note) => {
              const icon = definition.noteIcons[note.kind] ?? definition.severalNotesIcon;
              if (changing?.noteId === note.id) {
                const { draft } = changing;
                const set = (next: Partial<Draft>) => setChanging({ noteId: note.id, draft: { ...draft, ...next } });
                return (
                  <li key={note.id} className="hs-note hs-note-changing">
                    <KindChoice kinds={kinds} icons={definition.noteIcons} fallback={definition.severalNotesIcon} value={draft.kind} label={`Kind of ${note.about}`} onChange={(kind) => set({ kind })} />
                    <textarea
                      autoFocus
                      className="hs-field hs-note-text-field"
                      aria-label={`Text about ${note.about}`}
                      rows={3}
                      value={draft.text}
                      onChange={(event) => set({ text: event.target.value.replace(/\r?\n/g, " ") })}
                      onKeyDown={(event) => {
                        if (event.key === "Enter") {
                          event.preventDefault();
                          save();
                        }
                      }}
                    />
                    <textarea className="hs-field hs-field-multiline" aria-label={`Detail about ${note.about}`} rows={2} value={draft.detail} onChange={(event) => set({ detail: event.target.value })} />
                    <div className="hs-note-buttons">
                      <button type="button" className="dialog-button dialog-button-neutral hs-note-button" onClick={() => setChanging(null)}>
                        Cancel
                      </button>
                      <button type="button" className="dialog-button dialog-button-primary hs-note-button" onClick={save} disabled={draft.text.trim() === ""}>
                        {words.change}
                      </button>
                    </div>
                  </li>
                );
              }
              return (
                <NoteRow key={note.id} resolved={note.resolved} actions={canEdit && (
                  <>
                    <NoteAction icon={note.resolved ? REOPEN_ICON : RESOLVE_ICON} label={note.resolved ? words.reopen : words.resolve} onClick={() => onGesture(operations.resolve, note.id, {})} />
                    <NoteAction icon={CHANGE_ICON} label={words.change} onClick={() => setChanging({ noteId: note.id, draft: { kind: note.kind, text: note.text, detail: note.detail } })} />
                    <NoteAction icon={REMOVE_ICON} label={words.remove} onClick={() => onGesture(operations.remove, note.id, {})} />
                  </>
                )}>
                  <div className="hs-note-head">
                    <span className={`mdi ${icon}`} role="img" aria-label={note.kind} title={note.kind} />
                    <span className="hs-number">{note.about}</span>
                    <span className="hs-note-text">{note.text}</span>
                  </div>
                  {note.detail !== "" && <p className="hs-note-detail">{note.detail}</p>}
                </NoteRow>
              );
            })}
          </ul>
          {canEdit && (
            <div className="hs-note-new">
              <KindChoice kinds={kinds} icons={definition.noteIcons} fallback={definition.severalNotesIcon} value={adding.kind} label={`Kind of the new one about ${number}`} onChange={(kind) => setAdding({ ...adding, kind })} />
              <textarea
                className="hs-field hs-note-text-field"
                aria-label={`${words.add} about ${number}`}
                placeholder={words.add}
                rows={3}
                value={adding.text}
                onChange={(event) => setAdding({ ...adding, text: event.target.value.replace(/\r?\n/g, " ") })}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.preventDefault();
                    add();
                  }
                }}
              />
              <div className="hs-note-buttons">
                <button type="button" className="dialog-button dialog-button-primary hs-note-button" onClick={add} disabled={adding.text.trim() === ""}>
                  Add
                </button>
              </div>
            </div>
          )}
        </div>,
        document.body,
      )}
    </span>
  );
}

/**
 * One note with its controls: beside it, one above the other, when it takes several lines, and under it, side by side,
 * when it takes one, both at its right.
 */
function NoteRow({ resolved, actions, children }: { resolved: boolean; actions: ReactNode; children: ReactNode }) {
  const body = useRef<HTMLDivElement>(null);
  const [tall, setTall] = useState(false);
  useLayoutEffect(() => {
    const element = body.current;
    if (element === null) {
      return;
    }
    const line = Number.parseFloat(getComputedStyle(element).lineHeight) || 20;
    setTall(element.getBoundingClientRect().height > line * 1.5);
  });
  return (
    <li className={`hs-note${resolved ? " hs-note-resolved" : ""}${tall ? " hs-note-tall" : ""}`}>
      <div ref={body} className="hs-note-body">
        {children}
      </div>
      {actions !== false && <div className="hs-note-actions">{actions}</div>}
    </li>
  );
}

function NoteAction({ icon, label, onClick }: { icon: string; label: string; onClick: () => void }) {
  return <button type="button" className={`mdi ${icon} hs-note-action`} aria-label={label} title={label} onClick={onClick} />;
}

interface KindChoiceProps {
  kinds: readonly string[];
  /** The icon of each kind, and the one for a kind without its own. */
  icons: Readonly<Record<string, string>>;
  fallback: string;
  value: string;
  label: string;
  onChange: (kind: string) => void;
}

/** The kind of a note, each with its icon to its left: a button that opens the kinds, chosen by click or by keys. */
function KindChoice({ kinds, icons, fallback, value, label, onChange }: KindChoiceProps) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(Math.max(0, kinds.indexOf(value)));
  const iconOf = (kind: string) => icons[kind] ?? fallback;
  const choose = (kind: string | undefined) => {
    setOpen(false);
    if (kind !== undefined && kind !== value) {
      onChange(kind);
    }
  };
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      if (!open) {
        setOpen(true);
        setActive(Math.max(0, kinds.indexOf(value)));
        return;
      }
      setActive((current) => (current + step + kinds.length) % kinds.length);
    } else if (open && (event.key === "Enter" || event.key === " ")) {
      event.preventDefault();
      choose(kinds[active]);
    } else if (open && event.key === "Escape") {
      // Closes the kinds only, not the list around them.
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
    }
  };
  return (
    <div className="hs-kind" onKeyDown={onKeyDown}>
      <button
        type="button"
        role="combobox"
        className="hs-field hs-kind-button"
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => {
          setActive(Math.max(0, kinds.indexOf(value)));
          setOpen((current) => !current);
        }}
      >
        <span className={`mdi ${iconOf(value)}`} aria-hidden="true" />
        <span className="hs-kind-name">{value}</span>
        <span className="mdi mdi-chevron-down hs-kind-chevron" aria-hidden="true" />
      </button>
      {open && (
        <ul className="hs-kind-list" role="listbox" aria-label={label}>
          {kinds.map((kind, at) => (
            <li
              key={kind}
              role="option"
              aria-selected={kind === value}
              className={`hs-kind-option${at === active ? " hs-kind-active" : ""}`}
              onPointerEnter={() => setActive(at)}
              onClick={() => choose(kind)}
            >
              <span className={`mdi ${iconOf(kind)}`} aria-hidden="true" />
              <span>{kind}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
