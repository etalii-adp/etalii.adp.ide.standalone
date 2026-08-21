import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ProjectGridPage } from "./ProjectGridPage";

const listProjects = vi.fn();
const removeProject = vi.fn();
const addProject = vi.fn();

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({ transport: {} }),
}));

vi.mock("@connectrpc/connect", () => ({
  createClient: () => ({ listProjects, removeProject, addProject }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

function project(idByte: number, name: string) {
  return {
    id: { value: new Uint8Array([idByte]) },
    name,
    displayPath: `C:\\projects\\${name}`,
  };
}

describe("ProjectGridPage removal confirmation", () => {
  it("asks for confirmation before removing a project, naming it and clarifying disk files are kept", async () => {
    listProjects.mockResolvedValue({ projects: [project(1, "Alpha")] });

    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Alpha");

    fireEvent.click(screen.getByRole("button", { name: "Remove Alpha" }));

    expect(screen.getByText("Remove project?")).not.toBeNull();
    expect(
      screen.getByText(
        "Are you really sure that you want to remove project 'Alpha'? It will not be deleted from disk but only from the project grid.",
      ),
    ).not.toBeNull();
    expect(document.querySelector(".dialog-header-icon.mdi-trash-can-outline")).not.toBeNull();
    expect(removeProject).not.toHaveBeenCalled();
  });

  it("only calls removeProject after the Yes button is clicked, with a red confirm button", async () => {
    listProjects.mockResolvedValue({ projects: [project(2, "Beta")] });
    removeProject.mockResolvedValue({});

    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Beta");

    fireEvent.click(screen.getByRole("button", { name: "Remove Beta" }));
    const yesButton = screen.getByRole("button", { name: "Yes" });
    expect(yesButton.className).toContain("dialog-button-danger");

    fireEvent.click(yesButton);

    expect(removeProject).toHaveBeenCalledTimes(1);
    expect(removeProject).toHaveBeenCalledWith({ projectId: { value: project(2, "Beta").id.value } });
  });

  it("does not remove the project when No is clicked", async () => {
    listProjects.mockResolvedValue({ projects: [project(3, "Gamma")] });

    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Gamma");

    fireEvent.click(screen.getByRole("button", { name: "Remove Gamma" }));
    fireEvent.click(screen.getByRole("button", { name: "No" }));

    expect(removeProject).not.toHaveBeenCalled();
    expect(screen.queryByText("Remove project?")).toBeNull();
  });
});
