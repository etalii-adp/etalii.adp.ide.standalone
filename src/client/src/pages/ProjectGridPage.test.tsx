import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ProjectGridPage } from "./ProjectGridPage";

const listProjects = vi.fn();
const removeProject = vi.fn();
const addProject = vi.fn();
const logout = vi.fn();

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({ transport: {}, logout }),
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
    // Arrange.
    listProjects.mockResolvedValue({ projects: [project(1, "Alpha")] });

    // Arrange, continued.
    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Alpha");

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Remove Alpha" }));

    // Assert.
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
    // Arrange.
    listProjects.mockResolvedValue({ projects: [project(2, "Beta")] });
    removeProject.mockResolvedValue({});

    // Arrange, continued.
    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Beta");

    // Arrange, continued.
    fireEvent.click(screen.getByRole("button", { name: "Remove Beta" }));
    const yesButton = screen.getByRole("button", { name: "Yes" });
    expect(yesButton.className).toContain("dialog-button-danger");

    // Act.
    fireEvent.click(yesButton);

    // Assert.
    expect(removeProject).toHaveBeenCalledTimes(1);
    expect(removeProject).toHaveBeenCalledWith({ projectId: { value: project(2, "Beta").id.value } });
  });

  it("does not remove the project when No is clicked", async () => {
    // Arrange.
    listProjects.mockResolvedValue({ projects: [project(3, "Gamma")] });

    // Arrange, continued.
    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByText("Gamma");

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Remove Gamma" }));
    fireEvent.click(screen.getByRole("button", { name: "No" }));

    // Assert.
    expect(removeProject).not.toHaveBeenCalled();
    expect(screen.queryByText("Remove project?")).toBeNull();
  });
});

describe("ProjectGridPage sign out", () => {
  it("signs the user out when the header button is clicked", async () => {
    // Arrange.
    listProjects.mockResolvedValue({ projects: [] });

    // Arrange, continued.
    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByRole("button", { name: "Add project" });

    // Act.
    expect(logout).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    // Assert.
    expect(logout).toHaveBeenCalledTimes(1);
  });

  it("leads the header with the sign-out button", async () => {
    // Arrange.
    listProjects.mockResolvedValue({ projects: [] });

    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByRole("button", { name: "Add project" });

    // Act and assert, step by step.
    const header = document.querySelector(".projects-header");
    expect(header?.firstElementChild).toBe(screen.getByRole("button", { name: "Sign out" }));
  });

  it("offers sign out while the projects are still loading", () => {
    // Arrange.
    // Never resolves, so the component stays in its loading branch.
    listProjects.mockReturnValue(new Promise(() => {}));

    // Act.
    render(<ProjectGridPage onProjectSelected={() => {}} />);

    // Assert.
    expect(screen.getByText("Loading projects…")).not.toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
    expect(logout).toHaveBeenCalledTimes(1);
  });
});

describe("ProjectGridPage add dialog", () => {
  /** Opens the add dialog from the grid's "+" tile and returns its fields. */
  async function openAddDialog() {
    listProjects.mockResolvedValue({ projects: [] });

    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByRole("button", { name: "Add project" });

    fireEvent.click(screen.getByRole("button", { name: "Add project" }));

    return {
      name: screen.getByPlaceholderText("My project"),
      path: screen.getByPlaceholderText("C:\\path\\to\\project"),
      // The tile and the dialog's confirm button share the "Add project" name, so
      // pick the one in the footer rather than the tile that opened the dialog.
      submit: screen
        .getAllByRole("button", { name: "Add project" })
        .find((button) => button.className.includes("dialog-button")) as HTMLElement,
    };
  }

  it("keeps the form out of the grid until the add tile is used", async () => {
    // Arrange.
    listProjects.mockResolvedValue({ projects: [] });

    // Act.
    render(<ProjectGridPage onProjectSelected={() => {}} />);
    await screen.findByRole("button", { name: "Add project" });

    // Assert.
    expect(screen.queryByPlaceholderText("My project")).toBeNull();
    expect(document.querySelector(".project-card-add-button .mdi-plus")).not.toBeNull();
  });

  it("disables Add project until both a name and a path are provided", async () => {
    // Arrange.
    const { name, path, submit } = await openAddDialog();

    // Act and assert, step by step.
    expect(submit).toHaveProperty("disabled", true);

    fireEvent.change(name, { target: { value: "Alpha" } });
    expect(submit).toHaveProperty("disabled", true);

    fireEvent.change(path, { target: { value: "C:\\projects\\alpha" } });
    expect(submit).toHaveProperty("disabled", false);
  });

  it("treats whitespace-only input as missing", async () => {
    // Arrange.
    const { name, path, submit } = await openAddDialog();

    // Act and assert, step by step.
    fireEvent.change(name, { target: { value: "   " } });
    fireEvent.change(path, { target: { value: "C:\\projects\\alpha" } });
    expect(submit).toHaveProperty("disabled", true);

    fireEvent.change(name, { target: { value: "Alpha" } });
    fireEvent.change(path, { target: { value: "\\\\" } });
    expect(submit).toHaveProperty("disabled", true);
  });

  it("adds the project with its path split into segments, then closes", async () => {
    // Arrange.
    addProject.mockResolvedValue({ result: { case: "project", value: {} } });
    const { name, path, submit } = await openAddDialog();

    fireEvent.change(name, { target: { value: "  Alpha  " } });
    fireEvent.change(path, { target: { value: "C:\\projects\\alpha" } });
    fireEvent.click(submit);

    // Act and assert, step by step.
    expect(addProject).toHaveBeenCalledWith({
      name: "Alpha",
      path: { segments: ["C:", "projects", "alpha"] },
    });

    await screen.findByRole("button", { name: "Add project" });
    expect(screen.queryByPlaceholderText("My project")).toBeNull();
  });

  it("keeps the dialog open and shows the error when the backend rejects the add", async () => {
    // Arrange.
    addProject.mockResolvedValue({ result: { case: "error", value: { message: "Folder not found." } } });
    const { name, path, submit } = await openAddDialog();

    // Act.
    fireEvent.change(name, { target: { value: "Alpha" } });
    fireEvent.change(path, { target: { value: "C:\\nope" } });
    fireEvent.click(submit);

    // Assert.
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Folder not found.");
    expect(screen.getByPlaceholderText("My project")).not.toBeNull();
  });
});
