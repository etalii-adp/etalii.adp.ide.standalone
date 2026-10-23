import type { HierarchySurfaceNode } from "./api/hierarchySurfaceModel";

export interface HierarchySurfaceTrailProps {
  /** The document's title, which leads back to the whole page. */
  title: string;
  /** The levels above the focused one, from the top down, and the focused one last. */
  path: readonly HierarchySurfaceNode[];
  onFocus: (id: string) => void;
}

/**
 * The trail above a focused page (FR-008, R10): the document's title, then each item above the focused one by number
 * and title, each a link that focuses it. Focus is the page's own and is never sent.
 */
export function HierarchySurfaceTrail({ title, path, onFocus }: HierarchySurfaceTrailProps) {
  return (
    <nav className="hs-trail" aria-label="Trail">
      <button type="button" className="hs-trail-link" onClick={() => onFocus("")}>
        {title === "" ? "Document" : title}
      </button>
      {path.map((node, index) => (
        <span key={node.id} className="hs-trail-step">
          <span className="mdi mdi-chevron-right hs-trail-separator" aria-hidden="true" />
          {index === path.length - 1 ? (
            <span className="hs-trail-current" aria-current="page">
              {node.number} {node.title}
            </span>
          ) : (
            <button type="button" className="hs-trail-link" onClick={() => onFocus(node.id)}>
              {node.number} {node.title}
            </button>
          )}
        </span>
      ))}
    </nav>
  );
}
