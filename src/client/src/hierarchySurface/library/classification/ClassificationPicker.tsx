import { useEffect, useRef, useState } from "react";
import { SearchList } from "@client/components/SearchList";
import { OptionTag } from "@client/table/library/cells/OptionTag";
import { OptionsEditor } from "@client/table/library/header/OptionsEditor";
import type { HierarchySurfaceNode, HierarchySurfaceOption } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceLabelPick } from "../definition/hierarchySurfaceDefinition";
import { listColumn, listRaise, lowerAscii, valuesOf } from "../labels/listEditing";

/** The class of the picker's list, which a press outside it closes. */
export const PICKER_LIST = "hs-pick-list";

export interface ClassificationPickerProps {
  /** The label's name, as the nodes' values name it. */
  label: string;
  pick: HierarchySurfaceLabelPick;
  node: HierarchySurfaceNode;
  /** The label's list: its members in their order, implicit ones included. */
  options: readonly HierarchySurfaceOption[];
  /** Every node of the page, whose values say how many carry a member a delete would clear. */
  nodes: readonly HierarchySurfaceNode[];
  /** Whether the value and the list can be changed: false while the document cannot be. */
  canEdit: boolean;
  onGesture: (operation: string, targetId: string, params: Readonly<Record<string, string>>) => unknown;
}

/**
 * A label of one value, a requirement's category or state (FR-030, FR-031), as the Knowledge designer's selection
 * property of one: the value drawn as its rounded tag in its member's colour, or, while it has none, the way to give
 * one, shown on the hovered or selected box. Pressed, it opens the label's searchable list, where a member is picked or
 * the value cleared, and from where the list is edited in the table's options editor, a new member added there too.
 * Every change is one gesture; what the file then says is the page's next model.
 */
export function ClassificationPicker({ label, pick, node, options, nodes, canEdit, onGesture }: ClassificationPickerProps) {
  const { operations, params, words } = pick;
  const value = valuesOf(node, label)[0];
  const member = value === undefined ? undefined : options.find((option) => lowerAscii(option.name) === lowerAscii(value));
  const [open, setOpen] = useState(false);
  const [editorAt, setEditorAt] = useState<{ x: number; y: number } | null>(null);
  const ref = useRef<HTMLSpanElement>(null);

  // A press anywhere outside the list closes it; its search has the focus while it is open, so typing searches at once.
  useEffect(() => {
    if (!open) {
      return;
    }
    ref.current?.querySelector(`.${PICKER_LIST}`)?.scrollIntoView?.({ block: "nearest" });
    ref.current?.querySelector<HTMLInputElement>(`.${PICKER_LIST} input`)?.focus();
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && ref.current !== null && !ref.current.contains(event.target)) {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [open]);

  if (!canEdit) {
    return value === undefined ? null : <OptionTag label={value} color={member?.colour ?? "default"} />;
  }

  const set = (name: string) => {
    setOpen(false);
    if (value === undefined || lowerAscii(value) !== lowerAscii(name)) {
      onGesture(operations.set, node.id, { [params.value]: name });
    }
  };

  return (
    <span ref={ref} className="hs-pick">
      <span
        className={value === undefined ? "hs-pick-value hs-pick-empty" : "hs-pick-value"}
        role="button"
        tabIndex={0}
        aria-label={value === undefined ? `${words.value} of ${node.number}: none` : `${words.value} of ${node.number}: ${value}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={(event) => {
          event.stopPropagation();
          setOpen((current) => !current);
        }}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            event.stopPropagation();
            setOpen(true);
          }
        }}
      >
        {value === undefined ? (
          <>
            <span className="mdi mdi-plus" aria-hidden="true" />
            {words.value}
          </>
        ) : (
          <OptionTag label={value} color={member?.colour ?? "default"} />
        )}
      </span>
      {open && (
        <div
          className={PICKER_LIST}
          role="dialog"
          aria-label={`${words.value} of ${node.number}`}
          onKeyDown={(event) => event.stopPropagation()}
          onClick={(event) => event.stopPropagation()}
          onDoubleClick={(event) => event.stopPropagation()}
        >
          <SearchList
            label={`${words.value} of ${node.number}`}
            items={options.map((option) => ({ id: option.id, label: option.name }))}
            selected={member === undefined ? [] : [member.id]}
            placeholder="Search…"
            onPick={(id) => {
              const option = options.find((candidate) => candidate.id === id);
              if (option !== undefined) {
                set(option.name);
              }
            }}
            onClose={() => setOpen(false)}
          />
          {value !== undefined && (
            <button
              type="button"
              className="hs-pick-clear"
              onClick={() => {
                setOpen(false);
                onGesture(operations.clear, node.id, {});
              }}
            >
              <span className="mdi mdi-close" aria-hidden="true" />
              <span>{words.clear}</span>
            </button>
          )}
          <button
            type="button"
            className="hs-tag-edit-list"
            onClick={() => {
              // The editor takes the list's place, measured on the screen, since it is laid out against the window.
              const rect = ref.current?.querySelector(`.${PICKER_LIST}`)?.getBoundingClientRect();
              setEditorAt({ x: rect?.left ?? 0, y: rect?.top ?? 0 });
              setOpen(false);
            }}
          >
            <span className="mdi mdi-pencil-outline" aria-hidden="true" />
            <span>Edit {words.list.toLowerCase()}</span>
          </button>
        </div>
      )}
      {editorAt !== null && (
        <OptionsEditor
          column={listColumn(label, words.list, options, nodes)}
          position={editorAt}
          raise={listRaise({ ...operations, params }, options, onGesture)}
          onClose={() => setEditorAt(null)}
          deleteQuestion={(option, uses) => pick.deletion.message(option.name, uses)}
        />
      )}
    </span>
  );
}
