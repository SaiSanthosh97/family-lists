# TD-02 Lists and months

Ported to `Core.Lists` from the web version's tested logic (`period.ts`, `rollover.ts`, `productNames.ts`).

## Model
- `ShoppingList`: a monthly grocery list or a to-do list, in one group.
- `MonthList`: one month (`YYYY-MM`) of a monthly list.
  - Status: **Draft** (being reviewed) → **Final** (shopping) → **Shopped** (after a bill, or "Done, no bill").
  - An optional manual total.
- `ListItem`:
  - name, quantity, unit, notes;
  - *included* (needed this month) and *bought* (in the cart);
  - source: **Carried** from last month, **Added** by hand, or found on a **Bill**;
  - *skipped last month*, price, position.

To-do items have no month.

## Starting a month (rollover)
`Rollover.BuildCarryOver` takes the most recent month's items:
- everything comes forward, including what was found on the bill;
- items marked *not needed* last month still come forward, but are flagged and listed last;
- duplicate names (after normalising) collapse into one;
- prices and ticks are not copied, because spending is tracked per month.

**Converging ids (C-002).**
- The month's id is `Ids.Derive("month", listId, period)`.
- Each carried item's id is `Ids.Derive("item", monthId, normalisedName)`, the first 128 bits of a SHA-256.
- These records get the fixed version `Seeds.Version` (HLC `1/0/seed`), which any real edit beats.

So two phones that start the same month offline produce identical records, and sync has nothing to merge.

## Product names
`ProductNames` normalises names: lower case, punctuation and unit words removed, singular. It scores similarity by how many words two names share. It is used to match bill lines to list items and to group a product's price history.

## Importing a note (C-011)
Notes apps share plain text. Google Keep writes a checklist as `☐ Milk` / `☑ Eggs` under the title, and also puts the title in the share's subject. Samsung Notes and others use bullets, dashes, numbers or nothing, and Samsung Notes can also share a `.txt` file.

`NoteImport.Parse`:
- **Title:** the first line counts as the title when it has no checkbox or bullet while most other lines do, or when an empty line follows it.
- **Markers removed:** checkboxes (`☐ ☑ □ ✓ [ ] [x]`…), bullets (`- * • ○`…) and numbering (`1.` `2)`).
- **Ticked lines:** lines ticked in the note are marked as done.
- **Quantities:** "3 onions", "3x onions", "onions x3", "onions - 3", "onions (3)" and "onions: 3" are read as quantities. "Rice 5kg" and "Vitamin B12" stay as names.
- **Commas:** a single line with commas is split.
- **Clean-up:** lines without letters and duplicates (same normalised name) are dropped. At most 300 lines are read, and names are cut at 120 characters.

**The app:**
- **Share target:** the app accepts `text/plain` shares, and shared files that are plain text.
- **Opening Import:** the Import screen opens straight away. If the app was closed, it opens once the Lists screen has loaded.
- **Destination:** the list named like the note is chosen first, otherwise the first monthly list.
- **Starting unticked:** lines already on that list (or its open month), or ticked in the note.
- **Monthly lists:** items go into the open month. If the newest month is already shopped or older than this month, the next month is started.

## Tests
`tests/Core.Lists.Tests` covers:
- periods, including year ends and bad input;
- rollover: skipped items, bill items, duplicates, and the same keys on every phone;
- name matching, including not forcing a match between unrelated products;
- notes as Keep, Samsung Notes and other apps share them: titles, checkboxes, bullets, numbering, quantities, commas, duplicates, and empty or huge input.

The services are tested through real storage in `Storage.Sqlite.Tests`, and across phones in `Core.Sync.Tests`.
