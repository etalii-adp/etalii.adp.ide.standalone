import { AdpMark } from "./AdpMark";

type AdpLogoProps = {
  /** Size of the mark alongside the product name. */
  size?: number;
};

/**
 * The full ADP lockup: the mark alongside the product name.
 *
 * "EtAlii" carries the regular text colour and ".Adp" the muted one, so the name
 * reads as one word while the mark's highlight stays the only accent in the lockup.
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
