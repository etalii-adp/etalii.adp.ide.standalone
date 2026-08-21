# EtAlii.Adp

## spec-workflow

This repo uses the `.spec-workflow/` folder (steering docs, specs, approvals, implementation logs) to plan and track work before implementation.

- Whenever a set of files under `.spec-workflow/` is added, or removed (not edited) by the LLM, commit that change immediately in its own commit — don't leave it uncommitted or bundle it with unrelated changes (e.g. `.idea/workspace.xml`).
- Whenever a requirements, design, or tasks document is approved through the web dashboard, commit that document (and its associated approval-lifecycle files, e.g. under `.spec-workflow/approvals/`) at that point too — this applies even though approval only changes/removes files rather than adding a fresh set.
- Use a short, descriptive commit message in the style already used in this repo's history (e.g. "Bumped approvals.", "Added gRPC core communication specs: requirements and design documents.").

## Manual testing in a worktree

When manually running the app (backend `dotnet run` + client `npm run dev`) for verification from inside a git worktree (not the main checkout), change both the client's dev server port (`src/client/vite.config.ts`'s `server.port`) and the backend's `Client:DevServerUrl` (`src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json`) to a different, free pair of ports before starting either server — the main checkout's own dev servers may already be running on the defaults (5174 client / 5080 backend). Before merging the worktree back, revert both files to their original values so the merge never carries a stray port change into `develop`.
