# Knowledge designer examples

One table, *Cities*, three times: the same table kept as YAML, as JSON and as XML. Open any of
the three `cities.adp` files; each opens the data file beside it as a table.

| Folder | The data file | Its registration |
|---|---|---|
| `yaml/` | `cities.yaml` | `cities.adp` |
| `json/` | `cities.json` | `cities.adp` |
| `xml/` | `cities.xml` | `cities.adp` |

Beside each is `provinces.yaml` with its own `provinces.adp`: the table the cities' relation
*Province* points at, in YAML in all three folders, because a relation names its target by file
name and all three cities files name this one.

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

## Relations

The folder `relations/` holds two tables written for ADP, *Countries* and *Regions*, each with its
registration. Between them they show what the cities do not:

- **A two-way relation.** A region's *Country* names a row of `countries.yaml`, and *Regions* in
  the countries' table shows, for each country, the regions that name it. The values are in
  `regions.yaml` only; the countries' side holds none of its own.
- **A one-way relation.** A region's *Borders on* names countries too, and no country shows it.
- **A parent relation.** A country's *Part of* names another country of the same table, and the
  view *Nested*, which is grouped by that relation, shows the Netherlands and Belgium under Benelux.
- **Each kind of grouping.** The countries are grouped by a selection (*By continent*, which also
  hides a property, sets widths, sorts, filters and leaves out empty groups), by a multiple
  selection (*By language*, where Belgium stands under three headings) and by a checkbox
  (*Union*); the regions are grouped by a relation (*By country*).

## What these examples do not demonstrate

- **Nothing large.** A dozen rows say nothing about ten thousand; the module's tests make their own.
- **Nothing wrong.** No unknown option, no value of the wrong type, no entry without an id.
- **The relations in one format only.** *Countries* and *Regions* are YAML; the cities are the
  example that exists in all three formats.
