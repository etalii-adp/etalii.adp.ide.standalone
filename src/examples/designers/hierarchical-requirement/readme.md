# Hierarchical Requirement Designer examples

Two requirement documents. The requirements of a web shop are copied from the module's own examples in [`src/designers/hierarchical-requirement/examples`](../../../designers/hierarchical-requirement/examples), whose readme says where they come from and what they do not demonstrate. The security and privacy controls of NIST SP 800-53 are a large document, written from NIST's published data; [its readme](nist-sp-800-53/readme.md) says how, under what licence, and what it does not demonstrate.

| Folder | The document | Its registration |
|---|---|---|
| `web-shop/` | `web-shop.md` | `web-shop.adp` |
| `nist-sp-800-53/` | `nist-sp-800-53.md` | `nist-sp-800-53.adp` |

A requirement document is one Markdown file, recognised by the envelope on its first line: open `web-shop.md`, or `web-shop.adp` beside it, and it opens as one page of nested boxes. The registration holds only what the Markdown does not: the colours of the tags, categories and states, the entries kept collapsed and the stored filters. A document without one opens too, and the registration is created the first time an entry is collapsed.

`LICENSE.md` is the licence of the repository the web shop comes from, verbatim; `nist-sp-800-53/` carries its own.
