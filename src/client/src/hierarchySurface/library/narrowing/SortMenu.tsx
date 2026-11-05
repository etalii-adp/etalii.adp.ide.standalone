/** One key the entries can be sorted by, in the query's notation, and what the menu calls it. */
export interface SortKey {
  key: string;
  label: string;
}

export interface SortMenuProps {
  keys: readonly SortKey[];
  /** The key applied, or empty for the file's order. */
  value: string;
  onChange: (key: string) => void;
}

/**
 * The order of the requirements within each item (FR-020): the file's, or by one of the keys. It changes the query's
 * `sort:` word, so a stored filter keeps it, and it writes nothing.
 */
export function SortMenu({ keys, value, onChange }: SortMenuProps) {
  const known = value === "" || keys.some((key) => key.key === value);
  return (
    <label className="hs-sort">
      <span className="mdi mdi-sort" aria-hidden="true" />
      <select aria-label="Sort" value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">File order</option>
        {keys.map((key) => (
          <option key={key.key} value={key.key}>
            {key.label}
          </option>
        ))}
        {!known && <option value={value}>{value}</option>}
      </select>
    </label>
  );
}
