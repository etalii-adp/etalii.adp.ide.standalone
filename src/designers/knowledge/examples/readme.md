# The Knowledge designer's examples

The files here are copied, byte for byte, from [`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder `definitions/designers/examples/`, at commit `1559b53f13239c05b73748b151d3a2efad9dcc53` (`develop`, 2026-10-09):

- `cities.yaml`, `cities.json` and `cities.xml`: one table, *Cities*, written in each of the three formats a knowledge file comes in;
- `LICENSE.md`: that repository's `LICENSE`, verbatim.

`.gitattributes` keeps this folder from line-ending conversion, because the module's tests read these bytes and compare them.

## What these examples do not demonstrate

- **No registration.** The `.adp` file that makes a knowledge file a document of a project is not here yet, so the examples do not open from the showcase.
- **No second table.** `cities` has a relation to `provinces.yaml`, which does not exist: a relation whose target is missing is shown, and a resolved one is not.
- **Nothing large.** Twelve rows or so say nothing about ten thousand.
- **No file with something wrong in it**: no unknown key, no value of the wrong type, no missing id. The module's tests write those themselves.
