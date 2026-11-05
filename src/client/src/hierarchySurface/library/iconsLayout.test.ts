import { describe, expect, it } from "vitest";
import { BOX_SPACING, ICON_SIZE, iconsLayoutOf } from "./iconsLayout";

describe("the layout of a box's controls", () => {
  it("stands them in a column while the box or its first line is not measured", () => {
    // Act & Assert.
    expect(iconsLayoutOf(null, 100, 2)).toBe("column");
    expect(iconsLayoutOf(400, null, 2)).toBe("column");
  });

  it("stands them in a row when the first line and every control fit side by side, with the box's space around them", () => {
    // Arrange: a box 300 wide holds 288 inside its padding; three controls and the space before them take 66.
    const width = 300;
    const fits = width - 2 * BOX_SPACING - BOX_SPACING - 3 * ICON_SIZE;

    // Act & Assert.
    expect(iconsLayoutOf(width, fits, 3)).toBe("row");
    expect(iconsLayoutOf(width, fits + 1, 3)).toBe("column");
    // One control fewer leaves room for a longer first line.
    expect(iconsLayoutOf(width, fits + ICON_SIZE, 2)).toBe("row");
  });
});
