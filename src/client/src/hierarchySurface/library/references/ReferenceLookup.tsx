import { type FocusEvent, useEffect, useMemo, useRef } from "react";
import { SearchList } from "@client/components/SearchList";
import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";

/** The class of the lookup, by which the field it belongs to knows the focus has not left it. */
export const REFERENCE_LOOKUP = "hs-reference-lookup";

export interface ReferenceLookupProps {
  /** The levels and entries a reference can name: every node of the document that has a number. */
  candidates: readonly HierarchySurfaceNode[];
  /** A node was picked; its number is what the reference writes. */
  onPick: (number: string) => void;
  /** Escape: back to the text, with what was typed kept. */
  onClose: () => void;
  /** The focus left both the lookup and its field. */
  onLeave: (event: FocusEvent) => void;
}

/**
 * The lookup `#` opens in a description (FR-017): the levels and entries of the same document by number and title,
 * narrowed by either as the person types, on the Knowledge designer's searchable list. It takes the focus at once, so
 * typing goes on in it.
 */
export function ReferenceLookup({ candidates, onPick, onClose, onLeave }: ReferenceLookupProps) {
  const ref = useRef<HTMLDivElement>(null);
  const items = useMemo(() => candidates.map((node) => ({ id: node.number, label: `${node.number} ${node.title}` })), [candidates]);

  useEffect(() => {
    ref.current?.querySelector<HTMLInputElement>("input")?.focus();
  }, []);

  return (
    <div
      ref={ref}
      className={REFERENCE_LOOKUP}
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
      onBlur={(event) => {
        if (!(event.relatedTarget instanceof Node && ref.current?.contains(event.relatedTarget))) {
          onLeave(event);
        }
      }}
    >
      <SearchList label="Refer to" items={items} selected={[]} placeholder="Number or title" onPick={onPick} onClose={onClose} />
    </div>
  );
}
