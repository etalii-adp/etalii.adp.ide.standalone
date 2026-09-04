import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { LoginPage } from "./LoginPage";

// What is under test is the version line's contract (github-build-pipeline R3.1-R3.3):
// shown when the backend answers, absent - not stale, not invented - when it does not.
// The wire is a stand-in; the login flow itself has its own coverage elsewhere.
const describeProduct = vi.fn<() => Promise<{ version: string }>>();

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return { ...actual, createClient: () => ({ describeProduct }) };
});

vi.mock("../auth/AuthContext", () => {
  // One identity for the transport, which is what AuthContext actually guarantees: it memoises
  // the transport on a `[]`-stable callback and reads the token through a ref, so it is built
  // once. A fresh object per call would be a mock making a promise the real thing does not,
  // and a client memoised on it would then be rebuilt - churning the effect it keys.
  const transport = {};
  return { useAuth: () => ({ login: vi.fn(), transport }) };
});

describe("LoginPage", () => {
  beforeEach(() => {
    describeProduct.mockReset();
  });

  it("shows the backend's version below the login panel (R3.1)", async () => {
    // Arrange.
    describeProduct.mockResolvedValue({ version: "0.1.7-alpha+abc1234" });

    // Act.
    render(<LoginPage />);

    // Assert: the stamped version, verbatim - one source of truth (R3.2).
    expect((await screen.findByText("0.1.7-alpha+abc1234")).className).toBe("auth-version");
  });

  it("shows nothing when the call fails - never a stale or invented number (R3.3)", async () => {
    // Arrange.
    describeProduct.mockRejectedValue(new Error("backend unreachable"));

    // Act.
    render(<LoginPage />);
    await screen.findByText("Sign in", { selector: "h1" });

    // Assert.
    expect(document.querySelector(".auth-version")).toBeNull();
  });

  it("shows nothing for an empty answer (R3.3)", async () => {
    // Arrange: a backend built without a stamp answers honestly with "".
    describeProduct.mockResolvedValue({ version: "" });

    // Act.
    render(<LoginPage />);
    await screen.findByText("Sign in", { selector: "h1" });

    // Assert.
    expect(document.querySelector(".auth-version")).toBeNull();
  });
});
