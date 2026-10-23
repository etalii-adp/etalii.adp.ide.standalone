import type { HierarchySurfaceNode, HierarchySurfaceReference } from "../api/hierarchySurfaceModel";

export interface ReferenceBoxProps {
  /** The reference as the text writes it: "[REQ-0002]". */
  written: string;
  /** The node it names, or undefined when it names none. */
  target: HierarchySurfaceNode | undefined;
  /** Shows the target; absent for a reference that names none. */
  onReveal: (targetId: string) => void;
}

/**
 * A reference as it stands in a description (028's User Story 6): a box with its target's number and current title, which
 * shows the target when it is selected (FR-018). One that names nothing keeps the text as written and is marked broken;
 * the finding beside the page says why.
 */
export function ReferenceBox({ written, target, onReveal }: ReferenceBoxProps) {
  if (target === undefined) {
    return (
      <span className="hs-reference hs-reference-broken" role="img" aria-label={`${written} names nothing in this document`} title={`${written} names nothing in this document`}>
        <span className="mdi mdi-link-variant-off" aria-hidden="true" />
        {written}
      </span>
    );
  }
  return (
    <button
      type="button"
      className="hs-reference"
      title={`Show ${target.number}`}
      onClick={(event) => {
        event.stopPropagation();
        onReveal(target.id);
      }}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <span className="hs-number">{target.number}</span> {target.title}
    </button>
  );
}

/**
 * A description with each reference in it as a box, and the text between them as it is written. The references are the
 * model's, by their offsets in the text; one that does not fit the text is left as text.
 */
export function ReferenceText({
  text,
  references,
  nodeOf,
  onReveal,
}: {
  text: string;
  references: readonly HierarchySurfaceReference[];
  nodeOf: (id: string) => HierarchySurfaceNode | undefined;
  onReveal: (targetId: string) => void;
}) {
  const parts: (string | { written: string; targetId: string; at: number })[] = [];
  let at = 0;
  for (const reference of [...references].sort((a, b) => a.start - b.start)) {
    if (reference.start < at || reference.start + reference.length > text.length) {
      continue;
    }
    parts.push(text.slice(at, reference.start));
    parts.push({ written: text.slice(reference.start, reference.start + reference.length), targetId: reference.targetId, at: reference.start });
    at = reference.start + reference.length;
  }
  parts.push(text.slice(at));
  return (
    <>
      {parts.map((part) =>
        typeof part === "string" ? (
          part
        ) : (
          <ReferenceBox key={`${part.at}`} written={part.written} target={part.targetId === "" ? undefined : nodeOf(part.targetId)} onReveal={onReveal} />
        ),
      )}
    </>
  );
}
