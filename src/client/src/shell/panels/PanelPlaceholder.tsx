import { useEffect, useState } from "react";

export interface PanelPlaceholderProps {
  title: string;
  description: string;
  futureSpec: string;
}

const SHIM_DURATION_MS = 600;

export function PanelPlaceholder({ title, description, futureSpec }: PanelPlaceholderProps) {
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const timer = setTimeout(() => setLoading(false), SHIM_DURATION_MS);
    return () => clearTimeout(timer);
  }, []);

  if (loading) {
    return (
      <div className="panel-placeholder-shim" aria-busy="true" aria-label={`Loading ${title}`}>
        <div className="panel-placeholder-shim-line panel-placeholder-shim-line-title" />
        <div className="panel-placeholder-shim-line" />
        <div className="panel-placeholder-shim-line" />
      </div>
    );
  }

  // TODO(diagram-ide-mockup): replace this branch with the real panel content
  // identified by the futureSpec prop (e.g. "project-root-folder-explorer").
  return (
    <div className="panel-placeholder" data-future-spec={futureSpec}>
      <p className="panel-placeholder-title">{title}</p>
      <p className="panel-placeholder-description">{description}</p>
    </div>
  );
}
