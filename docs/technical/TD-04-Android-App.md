# TD-04 Android app

`src/Apps/FamilyLists.App` is a .NET 10 MAUI app for Android 8.0+. It uses MVVM (CommunityToolkit.Mvvm) and compiled XAML bindings, and is structured like Travel Vault.

## Start-up and storage
- **First launch:** the Welcome screen asks for a name. The phone then gets an encrypted SQLite database (SQLite3 Multiple Ciphers). Its master key is wrapped by an Android Keystore key (`familylists_master_wrap`).
- **Later launches:** the database opens silently. There is no app lock.
- **Wiring:** `VaultHost` owns the open database and builds `AppServices`, wired the same way as the tests.

## Screens
- **Lists:** group switcher, monthly lists with this month's status, to-do lists, and a banner for join requests.
- **Month:**
  - move between months, or start one;
  - review: untick, add, Finalize;
  - shop: tick items, with progress;
  - finish: spend, bills, what was bought or not, and a manual total.
- **Item:** name, quantity, unit, notes and price. Monthly items can be removed for good; to-do items can be deleted.
- **Bill:** camera, gallery, file or shared in → reading → review → confirm.
- **Spending:** monthly bars, a breakdown per product, and price history.
- **To-do list:** tick, add, clear done.
- **Group:**
  - members, with when each was last seen;
  - invite by QR or text;
  - join requests with the safety code: approve or decline;
  - remove a member, leave, rename, set the currency;
  - sync status.
- **Join:** scan a QR code, or paste an invite and see the code to compare.
- **Settings:** name, optional Claude key, sync now, check for updates, about.

Pages derive from `VaultPage`. It reloads the view model when a sync brings changes while the page is visible. Edit screens load only once, so a sync can't overwrite what's being typed (C-004).

## Background and updates
- `SyncScheduler` runs sync (see TD-01).
- `Device.BackgroundWork` schedules the background job every 15 minutes.
- `UpdateService` checks GitHub Releases once a day and on request. It checks the APK's SHA-256 before installing.

## Sharing a bill in
`MainActivity` accepts images and PDFs shared from other apps (`Device.Files`). The bill screen picks them up from `Inbox`.

## Testing on the emulator
- **Build:** add `-p:EmulatorBuild=true` to build the Release app for x86-64.
- **Setup:** the app on an emulator, plus a console "second phone" that uses the same Core libraries, both on the real ntfy.sh.
- **Covered:**
  - creating a group and joining by QR;
  - adding items on both phones;
  - finalizing, and ticks arriving on the other phone;
  - reading and confirming a bill;
  - carrying the list over to next month.
- **Logs:** sync results and problems go to the Android log with the tag `FamilyLists` (C-006).
