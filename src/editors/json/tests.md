# json - manual checks

The checks of the JSON editor that only a browser can answer, because jsdom shows neither a colour nor a tooltip (json-editor design, *End-to-End Testing*). Each names the criteria it holds. The format and the assumptions about a developer build are those of the root [tests.md](../../../tests.md).

Every check is run against a project holding a copy of this module's `examples/` folder.

## An example with a schema opens in colour, in both themes (Requirements 1.2, 3.1 to 3.3)

- **Preconditions**: backend and client running from a local build; the project above.
- **Actions**: open `service.json`; read the colour of a property name, a string value, a number, `true` and a brace; switch the theme between light and dark without reopening the file.
- **Expected**: the five differ from one another as the nine `--color-syntax-*` tokens say; the property name and the string value beside it differ; every colour follows the theme switch; the status line names "Service settings"; the browser console shows no error from the editor.
- **Result**: **Pass** (2026-10-12, developer build: Debug, `developer` environment, bypassed sign-in, headless Chromium). Status line "Service settings" in both themes. Measured colours, dark then light: property name rgb(121,192,255) / rgb(5,80,174); string value rgb(126,231,135) / rgb(10,122,58); number rgb(255,166,87) / rgb(154,74,0); `true` rgb(210,168,255) / rgb(130,80,223); punctuation rgb(201,209,217) in dark. Every colour changed with the theme switch without reopening the file. A brace at the caret reads in the text colour because it carries the matching-bracket highlight; the other punctuation carries the punctuation colour. No console error or warning.

## A broken document shows each finding three ways (Requirements 4.4, 4.5, 6.1 to 6.3)

- **Preconditions**: as above.
- **Actions**: open `service-broken.json`; look at the text, the gutter and the status line; rest the pointer on an underlined value; move the caret to it and press F8.
- **Expected**: each break of the schema is underlined, errors and warnings in different line styles; its line has a gutter mark; the status line counts the errors and the warnings; the sentence shows under the pointer and from the keyboard; `$schema` itself is not reported.
- **Result**: **Pass** (2026-10-12, developer build: Debug, `developer` environment, bypassed sign-in, headless Chromium). Errors underlined on `""`, `"8080"`, `"chatty"`, the repeated array, the limits object, `-1` and `"region"`; the warning on `"verbose": true` in its own line style. 8 gutter marks; status line "7 errors, 1 warning". Hover showed the sentence and the property's description; F8 at the caret showed the sentence. `$schema` was not reported.

## A property completed from the keyboard alone (Requirements 7.1, 7.4, 7.5)

- **Preconditions**: as above.
- **Actions**: in `service.json`, put the caret on a new line inside the root object, press Ctrl+Space, walk the list with the arrow keys and accept an item with Enter.
- **Expected**: the list offers the properties not yet present, each with its description beside it; the accepted property is inserted quoted, with its colon and the shape of its value, and with a comma where the object needs one.
- **Result**: **Pass** (2026-10-12, developer build: Debug, `developer` environment, bypassed sign-in, headless Chromium). Ctrl+Space on a new line inside the root object offered only `verbose`, the one property not yet present, with its description beside it. Enter inserted `"verbose": ,` with the caret on a tab stop for the value.

## A schema edited in a second tab re-validates the first (Requirement 5.8)

- **Preconditions**: as above.
- **Actions**: open `service-broken.json` and `service.schema.json` side by side; in the schema, change `"type": "integer"` of `port` to `"type": "string"` and save.
- **Expected**: without reopening `service-broken.json`, the finding on `"8080"` goes away and the error count drops by one.
- **Result**: **Pass** (2026-10-12, developer build: Debug, `developer` environment, bypassed sign-in, headless Chromium). After saving the schema with Ctrl+S in its own tab (file on disk confirmed changed), `service-broken.json` dropped its finding on `"8080"` without being reopened, and the status line went from "7 errors, 1 warning" to "6 errors, 1 warning".
