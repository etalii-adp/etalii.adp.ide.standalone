# Knowledge designer examples

One table, *Cities*, three times: the same table kept as YAML, as JSON and as XML. Open any of
the three `cities.adp` files; each opens the data file beside it as a table.

| Folder | The data file | Its registration |
|---|---|---|
| `yaml/` | `cities.yaml` | `cities.adp` |
| `json/` | `cities.json` | `cities.adp` |
| `xml/` | `cities.xml` | `cities.adp` |

A knowledge file is two files. The data file holds everything: the properties, the rows, the
views with their columns, sorts, filter and grouping. The `.adp` file beside it holds two lines
and exists only to say which file is a table and of which kind.

The table opens in its view *All cities*, which shows the cities of the Netherlands, largest
first, grouped by country. The view *Map* shows every row as the file has it.

What you change here is written to the data file as you change it, and the application's undo
takes it back. These are the showcase's copies, so editing them changes files under version
control.

## Where they come from

The three data files are copied from the module's own examples in
`src/designers/knowledge/examples/`, which are in turn copied from
[`etalii-adp/etalii.adp`](https://github.com/etalii-adp/etalii.adp), folder
`definitions/designers/examples/`. `LICENSE.md` is that repository's licence, verbatim.

## What these examples do not demonstrate

- **No second table.** *Cities* has a relation, *Province*, to a `provinces.yaml` that is not
  here: its value is shown as the id it holds.
- **No parent relation**, so no rows nested under rows.
- **Nothing large.** Two rows say nothing about ten thousand; the module's tests make their own.
- **Nothing wrong.** No unknown option, no value of the wrong type, no entry without an id.
