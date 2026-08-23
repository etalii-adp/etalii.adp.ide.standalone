import { AdpMark } from "./AdpMark";

type AdpLogoProps = {
  /** Size of the mark alongside the product name. */
  size?: number;
};

/**
 * The full ADP lockup: the mark alongside the product name.
 *
 * "EtAlii" carries the muted text colour and ".Adp" the regular one, so the product's
 * own name leads and the house name sits behind it, while the name still reads as one
 * word and the mark's highlight stays the only accent in the lockup.
 */
export function AdpLogo({ size = 34 }: AdpLogoProps) {
  return (
    <div className="adp-logo">
      <AdpMark size={size} />
      <span className="adp-logo-name">
        EtAlii<span className="adp-logo-suffix">.Adp</span>
      </span>
    </div>
  );
}
