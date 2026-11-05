import type { HierarchySurfaceNode } from "./api/hierarchySurfaceModel";

export interface HierarchySurfaceTrailProps {
  /** The document's title, which leads back to the whole page. */
  title: string;
  /** The levels down to the one the selection stands in, from the top down; empty for none. */
  path: readonly HierarchySurfaceNode[];
  /** The level that is the page's left edge, empty for the whole page, or null while the page shows what is inside it. */
  focused: string | null;
  onFocus: (id: string) => void;
}

/**
 * The trail above the page (FR-008, R10), always shown: the document's title, then the levels down to the selected node,
 * or to the focused level while nothing is selected, by number and title. Each is a link that makes it the page's left
 * edge, the title the whole page, except the one that already is. Focus is the page's own and is never sent.
 */
export function HierarchySurfaceTrail({ title, path, focused, onFocus }: HierarchySurfaceTrailProps) {
  return (
    <nav className="hs-trail" aria-label="Trail">
      <TrailStep text={title === "" ? "Document" : title} current={focused === ""} onFocus={() => onFocus("")} />
      {path.map((node) => (
        <span key={node.id} className="hs-trail-step">
          <span className="mdi mdi-chevron-right hs-trail-separator" aria-hidden="true" />
          <TrailStep text={`${node.number} ${node.title}`} current={node.id === focused} onFocus={() => onFocus(node.id)} />
        </span>
      ))}
    </nav>
  );
}

function TrailStep({ text, current, onFocus }: { text: string; current: boolean; onFocus: () => void }) {
  return current ? (
    <span className="hs-trail-current" aria-current="page">
      {text}
    </span>
  ) : (
    <button type="button" className="hs-trail-link" onClick={onFocus}>
      {text}
    </button>
  );
}
