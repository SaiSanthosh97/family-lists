# TD-03 Bills and spending

## Reading a receipt on the phone (default)
1. The photo is shrunk (`PhotoShrinker`) and passed to Google ML Kit text recognition. This works offline and returns words with their boxes.
2. `ReceiptParser` (`Core.Receipts`) groups the words into rows by their vertical centre. For each row it:
   - splits off the trailing numbers, including Indian digit grouping such as `1,23,456.00`;
   - reads them as `qty × rate = amount` when the numbers agree, and otherwise takes the last number as the amount. A number printed with paise ("33.00") is never taken as a quantity, so "33.00 66.00" is read as 2 × 33 when OCR missed the "2" (C-008);
   - skips headers, tax rows (GST/CGST/SGST), payment rows (CASH/UPI/CARD), change and bill numbers.

   It also picks out the total, subtotal, tax, discount, date (several formats) and store name: the first row above the products that is about as big as the biggest one. It isn't simply the tallest row, because letters like g and y make an address row's box taller than a shop name in capitals.
3. The result is a `ReceiptDraft`: store, date, total and lines.

## Reading with Claude (optional, C-003)
`Core.ClaudeReader` uses the official Anthropic C# SDK.
- **Request:** model `claude-opus-5-5`, medium effort, with a JSON schema for the answer: store, date, currency, total, and lines (name, quantity, unit price, amount).
- **Item names:** the list's current item names are sent along, so lines come back named the way the family names them.
- **Key:** the person's own API key. It is stored encrypted with the phone's key ring and never synced.
- **Errors:** a bad key, a rate limit, a refusal or an unreadable photo each get a plain message. If Claude fails, the app offers to read the bill on the phone instead.

## Confirming a bill
The review screen shows each line and what will happen to it:
- **match:** it matches a list item (best name match);
- **add:** it becomes a new item;
- **ignore:** it is skipped.

The person can change any of these, and edit names and amounts. `BillService.ConfirmAsync` then:
- turns "add" lines into items with source **Bill**, so next month's list includes them;
- sets each affected item's price to the sum of its confirmed lines, and marks it bought;
- marks the month **Shopped**;
- refuses to confirm the same bill twice.

A draft stays on the phone that made it. The photo is stored encrypted on that phone only; the confirmed bill and its lines sync to the group. Deleting a confirmed bill works the prices out again.

## Spending
`SpendingService` gives:
- each month's spend: from its bills, or else its manual total, or else the item prices;
- the difference between a bill's total and the sum of its lines;
- a per-product breakdown;
- a product's price history over the months.

For an expense list (TD-02), a month's spend is the sum of its expenses that were due, and the history shows each expense's amount over the months.

`Money` formats amounts in the group's currency; INR uses Indian digit grouping.

## Tests
- `Core.Receipts.Tests`: made-up word-box receipts in Indian supermarket layouts.
- `Core.Bills.Tests`: confirm rules, matching, money and spending.
- `Core.ClaudeReader.Tests`: the schema and parsing of the answer, without calling the API.
