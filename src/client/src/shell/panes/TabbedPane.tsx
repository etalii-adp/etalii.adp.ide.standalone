import { useState, type ReactNode } from "react";

export interface TabDef {
  id: string;
  label: string;
  icon?: string;
  content: ReactNode;
}

export interface TabbedPaneProps {
  tabs: TabDef[];
  defaultActiveTabId?: string;
}

export function TabbedPane({ tabs, defaultActiveTabId }: TabbedPaneProps) {
  const [activeTabId, setActiveTabId] = useState(defaultActiveTabId ?? tabs[0]?.id);
  const activeTab = tabs.find((tab) => tab.id === activeTabId) ?? tabs[0];

  return (
    <div className="tabbed-pane">
      {tabs.length > 1 && (
        <div className="tab-strip" role="tablist">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={tab.id === activeTab?.id}
              className={`tab${tab.id === activeTab?.id ? " tab-active" : ""}`}
              onClick={() => setActiveTabId(tab.id)}
            >
              {tab.icon && <span className={`mdi ${tab.icon}`} aria-hidden="true" />}
              {tab.label}
            </button>
          ))}
        </div>
      )}
      <div className="tabbed-pane-content">{activeTab?.content}</div>
    </div>
  );
}
