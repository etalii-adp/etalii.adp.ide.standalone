# yaml - browser checks

The checks of the yaml-editor design's End-to-End section: what only a running application shows. Each is run in a browser against a local build, signed in with the checked-in `admin`/`changeme` placeholder, on the showcase project `src/examples/`, in both themes where colour is concerned. Each records what was seen and when; a check that was not run says so.

## 1. The production build answers, hover included (Requirement 1.2)

1. Build the client for production (`npm run build` in `src/client`) and open the built page.
2. Open `editors/yaml/service-broken.yaml`. Wait for the underlines.
3. Hover over `name`; press Ctrl+Space on an empty line of the top-level mapping.

Expected: findings on the broken values, the hover text of the key, a list of keys; nothing from the language service in the console.

Seen 2026-10-12, task 1, in headless Chromium 141 against the production build with a temporary route running the same calls, without a backend: findings per document, the hover text, key and value completions; the console's only errors were 501 answers to the shell's sign-in calls. Without the alias the hover threw `a.basename is not a function`. Through the editor itself, 2026-10-13: the production build (`npm run build`, served by `vite preview` behind the backend) opened the file with 10 underlines and "10 errors, 1 warning" on the status line; hovering `name` showed "Service" and the key's description; Ctrl+Space at the end of the file listed `image`, `environment` and `limits`, the keys not yet present. The console had no errors.

## 2. Colour, in both themes (Requirement 3.1 to 3.3)

1. Open `editors/yaml/no-schema.yaml` and `editors/yaml/anchors.yaml` in the light theme, then switch to the dark theme.

Expected: keys, strings, numbers, booleans and null, comments, anchors and aliases, tags, the document markers and a block scalar each in their colour of the shared scheme; `1.10` and `0x1f` as numbers, `yes` and the date as strings; the colours follow the switch.

Seen 2026-10-13, read as each token's computed colour in both themes and switched live: keys, strings, numbers, `true`/`~` (keyword), comments, anchors and aliases, a typed `!!str` tag, `---` and `...` and the block scalars each in their scheme colour; `1.10` and `0x1f` numbers, `yes` and the date strings; every colour changed with the theme without a reload. It also found that `!!str 5` was coloured as a number, now fixed and held by `yamlScalarColour.test.tsx`.

## 3. Tab and Enter (Requirement 3.5, 3.6)

1. In `editors/yaml/service.yaml`, put the caret at the end of `ports:` and press Enter, then Tab, then Shift+Tab.
2. Type `- port: 80` and press Enter.

Expected: Enter goes one level deeper after `ports:`; Tab adds two spaces and never a tab; Enter after the item's first key lines up under it.

Seen 2026-10-13: Enter after `ports:` put the caret at column 2, Tab made it 4 with spaces, Shift+Tab took it back to 2; after `- port: 80` Enter put the caret at column 4, under `port`. No tab character was written.

## 4. A broken schema's three carriers (Requirement 6)

1. Open `editors/yaml/service-broken.yaml`.

Expected: each break shown as an underline, a gutter mark, and its sentence on hover, on F8 and in the findings panel (Ctrl+Shift+M); the status line counts them and names "Service"; the deprecated `memory` is a warning.

Seen 2026-10-13: underlines and gutter marks on lines 7 to 17, the hover sentence ("Incorrect type. Expected \"integer\"." on `many`), F8 jumping to the first finding ("Missing property \"image\"."), and Ctrl+Shift+M listing them; the status line read "10 errors, 1 warning" and "Service"; `memory` carried the warning mark.

## 5. Completion from the keyboard at two depths (Requirement 7)

1. In `editors/yaml/service.yaml`, empty the `restart: on-failure` line and press Ctrl+Space on it; accept `restart` and press Ctrl+Space after its colon.
2. Empty the `cpu: 0.5` line under `limits:`, keeping its indentation, and press Ctrl+Space on it.

Expected: on the top-level line the keys not yet present, `restart` among them and none already there; after `restart:` the three values the schema allows; under `limits` only `cpu`; every accepted item leaves YAML that reads.

Seen 2026-10-13: on the emptied top-level line only `restart` was offered (the deprecated `memory` is not offered); accepted, it wrote `restart: `, and Ctrl+Space there offered `always`, `never` and `on-failure`, giving `restart: always`. Under `limits` only `cpu` was offered, giving `  cpu: 0`. The file was not saved.

## 6. "Open as text" beside a pipeline's diagram (Requirement 2.3, 8)

1. Open an Azure DevOps pipeline of the showcase by its `.adp`; its diagram opens.
2. Open the same `.yml` with "Open as text"; it opens in the YAML editor.
3. Change a step's `displayName` there and save.

Expected: the diagram follows the change on disk.

Run 2026-10-13: double-clicking `steps-only.yml` opened its diagram, "Open as text" opened the same file in the YAML editor (status "No schema"), and a changed `displayName` saved to disk. The diagram showed only its stage box in this headless view, so whether it followed the change was **not seen in the browser**. The backend half is held by `YamlEditorResolution.Tests`: the `.adp` opens the diagram, "Open as text" resolves to this editor, and a save reaches the pipeline's document store as a change.
