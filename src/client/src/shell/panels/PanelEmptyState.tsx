export interface PanelEmptyStateProps {
  /** Omitted where a panel has only one thing to say, which is how the diagram tabs use it. */
  title?: string;
  description: string;
  /**
   * Extra attributes for the root element.
   *
   * This exists so the markup can have one home without the mockup scaffolding's meaning
   * coming with it. `PanelPlaceholder` marks its root `data-future-spec` and carries a
   * `TODO(diagram-ide-mockup)` saying the branch is to be replaced by a real panel; a real
   * panel that reused that component as-is would inherit both, and would then read as
   * scaffolding to whoever comes looking for what is left to build. So the markup lives here
   * and the mockup passes its own marker in, rather than the other way round.
   */
  rootAttributes?: Record<string, string>;
}

/**
 * The empty state a panel shows when it has nothing to show: one intentional sentence, not a
 * blank rectangle a reader has to interpret.
 *
 * It is one definition because it was three - two real panels rebuilding by hand what the
 * mockup placeholder already rendered, which is how the three drifted apart in the first place.
 */
export function PanelEmptyState({ title, description, rootAttributes }: PanelEmptyStateProps) {
  return (
    <div className="panel-placeholder" {...rootAttributes}>
      {title !== undefined && <p className="panel-placeholder-title">{title}</p>}
      <p className="panel-placeholder-description">{description}</p>
    </div>
  );
}
