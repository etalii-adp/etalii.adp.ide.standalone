import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { App } from "./App";
import type { Project } from "./generated/projects_pb";

// Stub AuthContext so Gate() sees an authenticated session without exercising
// the real login flow (Restriction: mock/stub AuthContext, not real login).
vi.mock("./auth/AuthContext", () => ({
  AuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAuth: () => ({
    isAuthenticated: true,
    transport: {},
    login: vi.fn(),
    logout: vi.fn(),
  }),
}));

// Stub ProjectGridPage so selecting a project doesn't require the real
// gRPC-backed project list (Restriction: mock/stub project selection state).
vi.mock("./pages/ProjectGridPage", () => ({
  ProjectGridPage: ({ onProjectSelected }: { onProjectSelected: (project: Project) => void }) => (
    <button
      type="button"
      onClick={() => onProjectSelected({ name: "Test Project" } as unknown as Project)}
    >
      Select Test Project
    </button>
  ),
}));

describe("App Gate()", () => {
  it("renders WorkspaceShell (not the old placeholder text) after a project is selected", () => {
    render(<App />);

    fireEvent.click(screen.getByText("Select Test Project"));

    expect(screen.getByText("Test Project")).toBeTruthy();
    expect(screen.getAllByRole("tablist")).toHaveLength(2);
    expect(screen.queryByText(/goes here \(adp-diagram-ide\)/)).toBeNull();
  });

  it("returns to the project grid when 'Back to projects' is used", () => {
    render(<App />);

    fireEvent.click(screen.getByText("Select Test Project"));
    expect(screen.getAllByRole("tablist")).toHaveLength(2);

    fireEvent.click(screen.getByText(/Back to projects/));

    expect(screen.getByText("Select Test Project")).toBeTruthy();
    expect(screen.queryAllByRole("tablist")).toHaveLength(0);
  });
});
