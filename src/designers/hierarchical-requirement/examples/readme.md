# The Hierarchical Requirement Designer's examples

The files here are copied, byte for byte, from [`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder `definitions/designers/examples/`, at commit `bee6f1f0c765964b5394b898442584ac30682f60` (`develop`, 2026-10-22, the commit etalii.adp spec 029's task T001 records):

- `web-shop.md`: one requirement document, the requirements of a web shop, in the Markdown file a requirement document is: the envelope, the mark line, the legend, six items at three levels with ten numbered requirements and one `REQ-NEW`, and four assessments;
- `web-shop.adp`: its registration, with the tag, category and state lists and their colours, two collapsed entries and two stored filters;
- `LICENSE.md`: that repository's `LICENSE`, verbatim.

Reading `web-shop.md` gives one finding, the `std.missingId` of `REQ-NEW`, which is numbered `REQ-0013` in memory, after the mark `REQ-0012`, and written with the next edit. It uses the three list markers, `-`, `*` and `+`, a description of several lines, a link and references in square brackets, and an assessment of each kind, one written in lower case and one without a checkbox.

`.gitattributes` keeps this folder from line-ending conversion, because the module's tests read these bytes and compare them.

## What these examples do not demonstrate

- **Nothing wrong but the placeholder.** No duplicate number, no bullet in no known form, no requirement outside an item, no reference to an entry the document does not have: the fixtures of `hierarchical-requirement-faults` hold those, and the module's tests read them.
- **No document without its registration.** Opening `web-shop.md` alone reads the lists from the body and the specification; the fixture `hierarchical-requirement-tag-list` shows that case.
- **No other section heading level and no heading in another case.** Both headings are `##` and written as the binding names them.
- **Nothing large.** Seventeen entries say nothing about a thousand.
