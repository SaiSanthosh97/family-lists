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

## Tests
`tests/Core.Lists.Tests` covers:
- periods, including year ends and bad input;
- rollover: skipped items, bill items, duplicates, and the same keys on every phone;
- name matching, including not forcing a match between unrelated products.

The services are tested through real storage in `Storage.Sqlite.Tests`, and across phones in `Core.Sync.Tests`.
