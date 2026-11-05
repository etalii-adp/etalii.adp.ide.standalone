import type { HierarchySurfaceFinding } from "./api/hierarchySurfaceModel";

export interface FindingListProps {
  findings: readonly HierarchySurfaceFinding[];
  /** Selects the node a finding stands at. */
  onSelect?: (id: string) => void;
}

const SEVERITY_ICONS: Readonly<Record<HierarchySurfaceFinding["severity"], string>> = {
  error: "mdi-alert-circle-outline",
  warning: "mdi-alert-outline",
  info: "mdi-information-outline",
};

/**
 * What reading the document reported, above the page: each finding at its line, and the node it stands at when it
 * names one. A finding at one bullet leaves the rest of the page as it is (SC-007).
 */
export function FindingList({ findings, onSelect }: FindingListProps) {
  if (findings.length === 0) {
    return null;
  }

  return (
    <ul className="hs-findings" aria-label="Findings">
      {findings.map((finding, index) => (
        <li key={`${finding.code}:${finding.line}:${index}`} className={`hs-finding hs-finding-${finding.severity}`}>
          <span className={`mdi ${SEVERITY_ICONS[finding.severity]}`} aria-label={finding.severity} role="img" />
          {finding.nodeId !== "" && onSelect !== undefined ? (
            <button type="button" className="hs-finding-link" onClick={() => onSelect(finding.nodeId)}>
              {finding.message}
            </button>
          ) : (
            <span>{finding.message}</span>
          )}
          {finding.line > 0 && <span className="hs-finding-line">line {finding.line}</span>}
        </li>
      ))}
    </ul>
  );
}
