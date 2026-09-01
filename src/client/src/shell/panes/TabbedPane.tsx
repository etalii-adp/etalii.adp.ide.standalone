import { useEffect, useLayoutEffect, useRef, useState, type MouseEvent, type ReactNode } from "react";

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

/** What an unmeasured tab is assumed to span until its first render says otherwise. */
const ESTIMATED_TAB_WIDTH = 120;

/** Room reserved for the overflow button whenever any tab has to hide behind it. */
const OVERFLOW_BUTTON_WIDTH = 40;

/**
 * A tab strip that never scrolls. When the open tabs outgrow the strip, the ones that do not
 * fit move behind a dropdown on the far right instead of behind a scrollbar - and the strip
 * keeps a focus history, so what hides is always the least recently used tab, never one the
 * user just looked at. Selecting a tab from the dropdown promotes it into the visible strip
 * in the demoted tab's place: the active tab is always in the primary list.
 */
export function TabbedPane({ tabs, defaultActiveTabId, activeTabId, onSelectTab, onCloseTab, emptyState }: TabbedPaneProps) {
  const [ownActiveTabId, setOwnActiveTabId] = useState(defaultActiveTabId ?? tabs[0]?.id);
  const controlled = activeTabId !== undefined;
  const currentId = controlled ? activeTabId : ownActiveTabId;
  const activeTab = tabs.find((tab) => tab.id === currentId) ?? tabs[0];

  // The strip's own display order: insertion order, adjusted only when a hidden tab is
  // promoted. Derived against the live tab set each render, so closes and opens elsewhere
  // never desynchronise it.
  const [order, setOrder] = useState<readonly string[]>([]);
  const ids = tabs.map((tab) => tab.id);
  const displayOrder = [...order.filter((id) => ids.includes(id)), ...ids.filter((id) => !order.includes(id))];

  // How many tabs hide behind the dropdown, and whether it is open.
  const [overflowCount, setOverflowCount] = useState(0);
  const [menuOpen, setMenuOpen] = useState(false);

  const stripRef = useRef<HTMLDivElement | null>(null);
  const tabWidthsRef = useRef(new Map<string, number>());
  // Bumped by the ResizeObserver purely to re-run the measuring layout effect below.
  const [, setResizeTick] = useState(0);

  // The focus history: most recent first. What it decides is which visible tab gives up its
  // place when a hidden one is promoted - always the least recently used.
  const recentRef = useRef<string[]>([]);
  useEffect(() => {
    if (currentId !== undefined) {
      recentRef.current = [currentId, ...recentRef.current.filter((id) => id !== currentId)];
    }
  }, [currentId]);

  useEffect(() => {
    const strip = stripRef.current;
    if (!strip || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(() => setResizeTick((tick) => tick + 1));
    observer.observe(strip);
    return () => observer.disconnect();
  }, [tabs.length > 0]);

  // The dropdown closes like any menu: on a click anywhere outside it, or on Escape.
  useEffect(() => {
    if (!menuOpen) {
      return;
    }

    const onPointerDown = (event: globalThis.MouseEvent) => {
      if (!(event.target instanceof Element) || !event.target.closest(".tab-overflow, .tab-overflow-menu")) {
        setMenuOpen(false);
      }
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMenuOpen(false);
      }
    };

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [menuOpen]);

  useLayoutEffect(() => {
    const strip = stripRef.current;
    if (!strip) {
      return;
    }

    // Record what actually rendered; hidden tabs keep their last measured width, and a tab
    // never rendered yet counts at an estimate until its first appearance corrects it.
    for (const element of strip.querySelectorAll<HTMLElement>("[data-tab-id]")) {
      const width = element.getBoundingClientRect().width;
      if (width > 0) {
        tabWidthsRef.current.set(element.dataset.tabId ?? "", width);
      }
    }

    const stripWidth = strip.getBoundingClientRect().width;
    if (stripWidth <= 0) {
      // Unmeasurable (jsdom, or not laid out yet): show everything rather than nothing.
      if (overflowCount !== 0) {
        setOverflowCount(0);
      }
      return;
    }

    const widthOf = (id: string) => tabWidthsRef.current.get(id) ?? ESTIMATED_TAB_WIDTH;
    const fits = (count: number, reserve: number) =>
      displayOrder.slice(0, count).reduce((sum, id) => sum + widthOf(id), 0) <= stripWidth - reserve;

    let visible = displayOrder.length;
    if (!fits(displayOrder.length, 0)) {
      while (visible > 1 && !fits(visible, OVERFLOW_BUTTON_WIDTH)) {
        visible -= 1;
      }
    }

    // The active tab must be in the primary list, never in the dropdown: promote it into
    // the place of the least recently used visible tab, which hides in its stead.
    if (currentId !== undefined && visible < displayOrder.length) {
      const activeIndex = displayOrder.indexOf(currentId);
      if (activeIndex >= visible) {
        const visibleIds = displayOrder.slice(0, visible);
        const byRecency = (id: string) => {
          const index = recentRef.current.indexOf(id);
          return index === -1 ? Number.POSITIVE_INFINITY : index;
        };
        const demoted = visibleIds.reduce((oldest, id) => (byRecency(id) > byRecency(oldest) ? id : oldest), visibleIds[0]);
        const next = displayOrder.filter((id) => id !== currentId);
        const slot = next.indexOf(demoted);
        next.splice(slot, 1, currentId);
        next.splice(visible, 0, demoted);
        setOrder(next);
        return; // recomputed on the re-render, against the new order
      }
    }

    const hidden = displayOrder.length - visible;
    if (hidden !== overflowCount) {
      setOverflowCount(hidden);
    }
  });

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

  const byId = new Map(tabs.map((tab) => [tab.id, tab]));
  const visibleTabs = displayOrder.slice(0, displayOrder.length - overflowCount).map((id) => byId.get(id)!).filter(Boolean);
  const hiddenTabs = displayOrder.slice(displayOrder.length - overflowCount).map((id) => byId.get(id)!).filter(Boolean);

  return (
    <div className="tabbed-pane">
      {(tabs.length > 1 || onCloseTab !== undefined) && (
        <div ref={stripRef} className="tab-strip" role="tablist">
          {visibleTabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              data-tab-id={tab.id}
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
          {hiddenTabs.length > 0 && (
            <div className="tab-overflow">
              <button
                type="button"
                className="tab-overflow-button"
                aria-label={`${hiddenTabs.length} more tabs`}
                aria-haspopup="menu"
                aria-expanded={menuOpen}
                onClick={() => setMenuOpen((open) => !open)}
              >
                <span className="mdi mdi-chevron-down" aria-hidden="true" />
              </button>
            </div>
          )}
        </div>
      )}
      {/* A sibling of the strip, not a child: the strip clips its overflow - that is the whole
          point of it - and a menu inside it would be clipped invisible along with the tabs. */}
      {menuOpen && hiddenTabs.length > 0 && (
        <div className="tab-overflow-menu" role="menu" style={{ top: stripRef.current?.offsetHeight ?? 36 }}>
          {hiddenTabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="menuitem"
              className="tab-overflow-item"
              title={tab.tooltip}
              onClick={() => {
                setMenuOpen(false);
                select(tab.id);
              }}
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
