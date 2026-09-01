import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { BoxElement } from "./box/BoxElement";
import { CenteredBoxElement } from "./centered-box/CenteredBoxElement";
import { StyledBoxElement } from "./styled-box/StyledBoxElement";
import { FrameElement } from "./frame/FrameElement";
import { SymbolElement } from "./symbol/SymbolElement";
import { SpanElement, type SpanElementClasses } from "./span/SpanElement";

function renderSvg(children: React.ReactNode) {
  return render(<svg>{children}</svg>);
}

const spanClasses: SpanElementClasses = {
  span: "t-span",
  moment: "t-moment",
  label: "t-label",
  hint: "t-hint",
  adorner: "t-adorner",
  anchor: "t-anchor",
  anchorHit: "t-anchor-hit",
};

describe("the shared element implementations", () => {
  it("draws a box element with its label and forwards the group props", () => {
    // Act.
    const onClick = vi.fn();
    const { container } = renderSvg(
      <BoxElement className="node" data-element-id="n1" x={10} y={20} width={120} height={40} label="Build" boxClassName="box" labelClassName="lbl" onClick={onClick}>
        <text className="extra">badge</text>
      </BoxElement>,
    );

    // Assert.
    const group = container.querySelector('[data-element-id="n1"]')!;
    expect(group.getAttribute("transform")).toBe("translate(10 20)");
    expect(group.querySelector("rect.box")).not.toBeNull();
    expect(group.querySelector("text.lbl")!.textContent).toBe("Build");
    expect(group.querySelector("text.extra")).not.toBeNull();
    fireEvent.click(group);
    expect(onClick).toHaveBeenCalled();
  });

  it("draws a centered box with centred text and corner indicators only when there are any", () => {
    // Act.
    const { container } = renderSvg(
      <>
        <CenteredBoxElement className="a" x={0} y={0} halfWidth={60} halfHeight={16} text="Idea" indicators="•" indicatorsClassName="ind" />
        <CenteredBoxElement className="b" x={0} y={50} halfWidth={60} halfHeight={16} text="Plain" />
      </>,
    );

    // Assert.
    expect(container.querySelector("g.a text")!.getAttribute("text-anchor")).toBe("middle");
    expect(container.querySelector("g.a .ind")).not.toBeNull();
    expect(container.querySelector("g.b .ind")).toBeNull();
  });

  it("draws each styled-box silhouette the document's styling asks for", () => {
    // Act.
    const { container } = renderSvg(
      <>
        <StyledBoxElement className="p" x={0} y={0} width={100} height={60} shape="Person" background="#123" color="#fff" name="User" />
        <StyledBoxElement className="c" x={0} y={100} width={100} height={60} shape="Cylinder" background="#123" color="#fff" name="Store" />
        <StyledBoxElement className="r" x={0} y={200} width={100} height={60} background="#123" color="#fff" name="App" typeLine="[Container]" description="Does things" />
      </>,
    );

    // Assert.
    expect(container.querySelector("g.p circle")).not.toBeNull();
    expect(container.querySelectorAll("g.c ellipse")).toHaveLength(2);
    const rounded = container.querySelector("g.r rect")!;
    expect(rounded.getAttribute("rx")).toBe("8");
    expect(container.querySelector("g.r")!.textContent).toContain("Does things");
  });

  it("draws a frame with its corner label", () => {
    // Act.
    const { container } = renderSvg(
      <FrameElement className="frame" x={0} y={0} width={400} height={300} label="Backend [System]" labelClassName="lbl" />,
    );

    // Assert.
    expect(container.querySelector("g.frame rect")!.getAttribute("width")).toBe("400");
    expect(container.querySelector("text.lbl")!.textContent).toBe("Backend [System]");
  });

  it("draws each symbol variant, with badges and the inertia bar when claimed", () => {
    // Act.
    const { container } = renderSvg(
      <>
        <SymbolElement className="s1" x={0} y={0} variant="square" label="Need" labelX={15} labelY={4} markClassName="mark" />
        <SymbolElement className="s2" x={0} y={50} variant="double-circle" label="Map" labelX={15} labelY={54} markClassName="mark" outerClassName="outer" />
        <SymbolElement className="s3" x={0} y={100} label="Kettle" labelX={15} labelY={104} badges={["buy", "inertia"]} inertia badgesClassName="badges" inertiaClassName="inertia" markClassName="mark" />
      </>,
    );

    // Assert.
    expect(container.querySelector("g.s1 rect.mark")).not.toBeNull();
    expect(container.querySelector("g.s2 .outer")).not.toBeNull();
    expect(container.querySelector("g.s3 circle.mark")).not.toBeNull();
    expect(container.querySelector("g.s3 .badges")!.textContent).toBe("buy · inertia");
    expect(container.querySelector("g.s3 line.inertia")).not.toBeNull();
  });

  it("renders a span's selection furniture only when selected, anchors painted after adorners", () => {
    // Act.
    const box = { x: 100, y: 30, width: 200, height: 36 };
    const { container, rerender } = renderSvg(
      <SpanElement className="el" box={box} label="Discovery" classes={spanClasses} />,
    );

    // Assert: unselected, no adorners and no anchors.
    expect(container.querySelector(".t-adorner")).toBeNull();
    expect(container.querySelector(".t-anchor-hit")).toBeNull();

    // Act: selected.
    rerender(
      <svg>
        <SpanElement className="el" box={box} label="Discovery" selected classes={spanClasses} />
      </svg>,
    );

    // Assert.
    // Two adorners, two visible anchors, two hit circles - and the anchors come after the
    // adorners in document order, which is what keeps them grabbable: SVG paints in order,
    // and the adorner strip used to cover the anchor down to a one-pixel sliver.
    expect(container.querySelectorAll(".t-adorner")).toHaveLength(2);
    expect(container.querySelectorAll(".t-anchor")).toHaveLength(2);
    expect(container.querySelectorAll(".t-anchor-hit")).toHaveLength(2);
    const children = [...container.querySelector("g.el")!.children].map((child) => child.getAttribute("class"));
    expect(children.indexOf("t-anchor")).toBeGreaterThan(children.lastIndexOf("t-adorner"));
  });

  it("draws a moment as a diamond with one adorner, and its label always beside it", () => {
    // Act.
    const box = { x: 100, y: 30, width: 18, height: 36 };
    const { container } = renderSvg(
      <SpanElement className="el" box={box} moment label="Go" selected classes={spanClasses} />,
    );

    // Assert.
    expect(container.querySelector(".t-moment")).not.toBeNull();
    expect(container.querySelectorAll(".t-adorner")).toHaveLength(1);
    expect(container.querySelector(".t-label")!.getAttribute("text-anchor")).toBe("start");
  });

  it("keeps a span's label centred, trimming it with an ellipsis when the box cannot hold it", () => {
    // Act.
    const { container } = renderSvg(
      <>
        <SpanElement className="wide" box={{ x: 100, y: 30, width: 200, height: 36 }} label="Discovery" classes={spanClasses} />
        <SpanElement className="narrow" box={{ x: 100, y: 90, width: 43, height: 36 }} label="A much longer label" classes={spanClasses} />
        <SpanElement className="sliver" box={{ x: 100, y: 150, width: 10, height: 36 }} label="Anything" classes={spanClasses} />
      </>,
    );

    // Assert.
    // What fits is untouched; what does not is trimmed to the same per-character estimate
    // that decides the fit - centred either way, never overflowing under the neighbours.
    expect(container.querySelector("g.wide .t-label")!.textContent).toBe("Discovery");
    expect(container.querySelector("g.wide .t-label")!.getAttribute("text-anchor")).toBeNull();
    // 43px minus the padding holds five characters: four survive plus the ellipsis.
    expect(container.querySelector("g.narrow .t-label")!.textContent).toBe("A mu…");
    expect(container.querySelector("g.narrow .t-label")!.getAttribute("text-anchor")).toBeNull();
    // A sliver of a box holds nothing but the ellipsis itself.
    expect(container.querySelector("g.sliver .t-label")!.textContent).toBe("…");
  });

  it("routes a span's resize and anchor gestures to their own handlers", () => {
    // Arrange.
    const onResizeStart = vi.fn();
    const onAnchorStart = vi.fn();
    const { container } = renderSvg(
      <SpanElement
        className="el"
        box={{ x: 100, y: 30, width: 200, height: 36 }}
        label="Discovery"
        selected
        classes={spanClasses}
        onResizeStart={onResizeStart}
        onAnchorStart={onAnchorStart}
      />,
    );

    // Act.
    fireEvent.mouseDown(container.querySelectorAll(".t-adorner")[1]);
    fireEvent.mouseDown(container.querySelectorAll(".t-anchor-hit")[0]);
    fireEvent.mouseDown(container.querySelectorAll(".t-anchor-hit")[1]);

    // Assert.
    // Each anchor names its side, because a relation dragged from the begin runs the other
    // way round than one dragged from the end.
    expect(onResizeStart).toHaveBeenCalledWith(expect.anything(), "right");
    expect(onAnchorStart).toHaveBeenNthCalledWith(1, expect.anything(), "left");
    expect(onAnchorStart).toHaveBeenNthCalledWith(2, expect.anything(), "right");
  });
});
