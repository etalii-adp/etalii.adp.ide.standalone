import { type KeyboardEvent, useEffect, useRef, useState } from "react";
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

/**
 * The list of a box's notes, opened from its icon (FR-016): each with its kind, the number it is about, its text and its
 * detail, resolved ones too; each changed, resolved or reopened, or removed; and a new one added at the bottom. A gesture
 * is sent as soon as it is made; what the file then says is the page's next model. Escape, wherever the focus is, or a
 * click outside closes it.
 */
export function NoteList({ notes, number, definition, canEdit, onGesture, nodeId, onClose }: NoteListProps) {
  const kinds = Object.keys(definition.noteIcons);
  const { operations, params, words } = definition.notes;
  const [changing, setChanging] = useState<{ noteId: string; draft: Draft } | null>(null);
  const [adding, setAdding] = useState<Draft>({ kind: kinds[0] ?? "", text: "", detail: "" });
  const ref = useRef<HTMLDivElement>(null);
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
    <div
      ref={ref}
      className="hs-note-list"
      role="dialog"
      aria-label={`${words.list} of ${number}`}
      onKeyDown={onKeyDown}
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <ul className="hs-notes">
        {notes.map((note) => {
          const icon = definition.noteIcons[note.kind] ?? definition.severalNotesIcon;
          if (changing?.noteId === note.id) {
            const { draft } = changing;
            const set = (next: Partial<Draft>) => setChanging({ noteId: note.id, draft: { ...draft, ...next } });
            return (
              <li key={note.id} className="hs-note hs-note-changing">
                <KindChoice kinds={kinds} value={draft.kind} label={`Kind of ${note.about}`} onChange={(kind) => set({ kind })} />
                <input autoFocus className="hs-field" aria-label={`Text about ${note.about}`} value={draft.text} onChange={(event) => set({ text: event.target.value })} onKeyDown={(event) => event.key === "Enter" && save()} />
                <textarea className="hs-field hs-field-multiline" aria-label={`Detail about ${note.about}`} rows={2} value={draft.detail} onChange={(event) => set({ detail: event.target.value })} />
                <div className="hs-note-actions">
                  <button type="button" className="hs-note-action" onClick={save} disabled={draft.text.trim() === ""}>
                    {words.change}
                  </button>
                  <button type="button" className="hs-note-action" onClick={() => setChanging(null)}>
                    Cancel
                  </button>
                </div>
              </li>
            );
          }
          return (
            <li key={note.id} className={`hs-note${note.resolved ? " hs-note-resolved" : ""}`}>
              <div className="hs-note-head">
                <span className={`mdi ${icon}`} role="img" aria-label={note.kind} title={note.kind} />
                <span className="hs-number">{note.about}</span>
                <span className="hs-note-text">{note.text}</span>
              </div>
              {note.detail !== "" && <p className="hs-note-detail">{note.detail}</p>}
              {canEdit && (
                <div className="hs-note-actions">
                  <button type="button" className="hs-note-action" onClick={() => onGesture(operations.resolve, note.id, {})}>
                    {note.resolved ? words.reopen : words.resolve}
                  </button>
                  <button type="button" className="hs-note-action" onClick={() => setChanging({ noteId: note.id, draft: { kind: note.kind, text: note.text, detail: note.detail } })}>
                    {words.change}
                  </button>
                  <button type="button" className="hs-note-action" onClick={() => onGesture(operations.remove, note.id, {})}>
                    {words.remove}
                  </button>
                </div>
              )}
            </li>
          );
        })}
      </ul>
      {canEdit && (
        <div className="hs-note-new">
          <KindChoice kinds={kinds} value={adding.kind} label={`Kind of the new one about ${number}`} onChange={(kind) => setAdding({ ...adding, kind })} />
          <input
            className="hs-field"
            aria-label={`${words.add} about ${number}`}
            placeholder={words.add}
            value={adding.text}
            onChange={(event) => setAdding({ ...adding, text: event.target.value })}
            onKeyDown={(event) => event.key === "Enter" && add()}
          />
          <button type="button" className="hs-note-action" onClick={add} disabled={adding.text.trim() === ""}>
            {words.add}
          </button>
        </div>
      )}
    </div>
  );
}

function KindChoice({ kinds, value, label, onChange }: { kinds: readonly string[]; value: string; label: string; onChange: (kind: string) => void }) {
  return (
    <select className="hs-field hs-note-kind" aria-label={label} value={value} onChange={(event) => onChange(event.target.value)}>
      {kinds.map((kind) => (
        <option key={kind} value={kind}>
          {kind}
        </option>
      ))}
    </select>
  );
}
