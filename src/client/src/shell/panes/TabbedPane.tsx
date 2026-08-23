import { useState, type MouseEvent, type ReactNode } from "react";

export interface TabDef {
  id: string;
  label: string;
  icon?: string;
  content: ReactNode;
  /** Shown on hover - the diagram tabs put the project-relative path here, so two files with one name stay distinguishable. */
  tooltip?: string;
}

export interface TabbedPaneProps {
  tabs: TabDef[];
  defaultActiveTabId?: string;
  /** Controlled mode: which tab is active. When absent the pane keeps its own state, exactly as before. */
  activeTabId?: string;
  onSelectTab?: (id: string) => void;
  /**
   * Makes every tab closable: a close control per tab, middle-click close, and the strip
   * stays visible even for a single tab - a closable tab must show its control.
   */
  onCloseTab?: (id: string) => void;
  /** Rendered in the content area when there are no tabs at all. */
  emptyState?: ReactNode;
}

export function TabbedPane({ tabs, defaultActiveTabId, activeTabId, onSelectTab, onCloseTab, emptyState }: TabbedPaneProps) {
  const [ownActiveTabId, setOwnActiveTabId] = useState(defaultActiveTabId ?? tabs[0]?.id);
  const controlled = activeTabId !== undefined;
  const currentId = controlled ? activeTabId : ownActiveTabId;
  const activeTab = tabs.find((tab) => tab.id === currentId) ?? tabs[0];

  const select = (id: string) => {
    if (!controlled) {
      setOwnActiveTabId(id);
    }
    onSelectTab?.(id);
  };

  const closeOnMiddleClick = (event: MouseEvent, id: string) => {
    if (onCloseTab && event.button === 1) {
      event.preventDefault();
      onCloseTab(id);
    }
  };

  if (tabs.length === 0) {
    return <div className="tabbed-pane">{emptyState !== undefined && <div className="tabbed-pane-content">{emptyState}</div>}</div>;
  }

  return (
    <div className="tabbed-pane">
      {(tabs.length > 1 || onCloseTab !== undefined) && (
        <div className="tab-strip" role="tablist">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={tab.id === activeTab?.id}
              className={`tab${tab.id === activeTab?.id ? " tab-active" : ""}`}
              title={tab.tooltip}
              onClick={() => select(tab.id)}
              onAuxClick={(event) => closeOnMiddleClick(event, tab.id)}
            >
              {tab.icon && <span className={`mdi ${tab.icon}`} aria-hidden="true" />}
              {tab.label}
              {onCloseTab && (
                <span
                  role="button"
                  aria-label={`Close ${tab.label}`}
                  className="tab-close mdi mdi-close"
                  onClick={(event) => {
                    // A close is not a select: the surviving tabs decide focus, not this one.
                    event.stopPropagation();
                    onCloseTab(tab.id);
                  }}
                />
              )}
            </button>
          ))}
        </div>
      )}
      <div className="tabbed-pane-content">{activeTab?.content}</div>
    </div>
  );
}
