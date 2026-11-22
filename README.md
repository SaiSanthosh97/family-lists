# Family Lists

A shared grocery and to-do app for a household. Lists live **on your phones**, sync between them **end-to-end encrypted**, and cost nothing. There are no accounts and no server.

- **The monthly grocery list copies itself forward.** You start each month from last month's list, review it together (untick what you don't need, add what's new), then press **Finalize**.
- **Shopping.** Tick things as they go in the cart; the other phones see the ticks within seconds while the app is open.
- **Bills.** Take a photo of the receipt, or share one in from WhatsApp, Gmail or your files. The phone reads the lines and prices itself (Google ML Kit, offline). Matched items get their price; things you bought that weren't on the list are added, so next month's list includes them. Optionally, Claude can read the bill instead, using **your own** Anthropic API key, which stays on that phone. This is off by default.
- **Spending is optional.** You get monthly totals, a breakdown per product and price history. No bill? Type the total in, or type a price on an item.
- **Groups.** Invite by QR code in person, or send an invite text and approve the request after comparing a 6-digit safety code. One phone can be in several groups, and groups can't read each other's data (each has its own key). Removing someone changes the group's key.
- **To-do lists**, shared inside the same group.

**Download:** [latest release](https://github.com/SaiSanthosh97/family-lists/releases/latest) (Android 8.0+). Install the `.apk`; later versions are offered in **Settings → Check for updates**.

Change log: [docs/CHANGES.md](docs/CHANGES.md) · Releasing: [docs/RELEASING.md](docs/RELEASING.md) · Technical write-ups: [docs/technical/](docs/technical/README.md)

The first version, a React web app on Supabase, is kept on the [`web-supabase`](https://github.com/SaiSanthosh97/family-lists/tree/web-supabase) branch.

## How it works

Everything is stored in an **encrypted SQLite database** on each phone; its key is protected by the Android Keystore. A change is signed by the phone that made it, encrypted with the group key and posted to a random topic on the free [ntfy.sh](https://ntfy.sh) relay. The other phones fetch it, check it and apply it. The relay only ever sees encrypted, fixed-size messages, and keeps them for about 12 hours. A phone that was away longer asks the others to resend the current state.

Sync runs:
- live while the app is open;
- a second after you change something;
- every minute while the app is open;
- about every 15 minutes in the background.

This is the same design as the author's *Travel Vault* and *Use It Before* apps, and several components are adapted from them (MIT, same author).

## Components

| Component | What it does |
|---|---|
| [`Core.Common`](src/Components/Core.Common) | Clock, ids (UUIDv7, derived ids), hybrid logical clock, JSON |
| [`Core.Security`](src/Components/Core.Security) | AES-256-GCM, HKDF, ECDSA/ECDH P-256, recipient boxes, key ring, file vault, safety code |
| [`Core.Families`](src/Components/Core.Families) | Groups, members, roles, currency, invites (QR / text) |
| [`Core.Lists`](src/Components/Core.Lists) | Lists, months, items; month rollover; product-name matching |
| [`Core.Bills`](src/Components/Core.Bills) | Bills and their lines, confirming a bill into the list, money, spending |
| [`Core.Receipts`](src/Components/Core.Receipts) | Reading a receipt from OCR word boxes: rows, quantities × rates, totals, date, store |
| [`Core.ClaudeReader`](src/Components/Core.ClaudeReader) | Optional: reads a receipt photo with Claude (official Anthropic SDK), the user's own key |
| [`Core.Vault`](src/Components/Core.Vault) | Session, change events, the synced-record contract |
| [`Core.Relay`](src/Components/Core.Relay) | ntfy client: publish, fetch, live stream |
| [`Core.Sync`](src/Components/Core.Sync) | Serverless sync: signed encrypted messages, joins, approvals, key rotation, catch-up |
| [`Core.AppUpdater`](src/Components/Core.AppUpdater) | Update check against GitHub Releases, verified by SHA-256 |
| [`Storage.Sqlite`](src/Components/Storage.Sqlite) | Encrypted SQLite (SQLite3 Multiple Ciphers), schema, repositories |
| [`Device.Vision`](src/Components/Device.Vision) | Android: ML Kit text recognition and barcodes, PDF pages, photo shrinking |
| [`Device.Security`](src/Components/Device.Security) | Android: Keystore-wrapped device key |
| [`Device.BackgroundWork`](src/Components/Device.BackgroundWork) | Android: periodic background sync (JobScheduler) |
| [`Device.Files`](src/Components/Device.Files) | Android: receiving a shared bill |

`Core.*` and `Storage.*` are plain .NET and unit-tested on any PC. `Device.*` hold the Android code behind Core interfaces. The app is [`src/Apps/FamilyLists.App`](src/Apps/FamilyLists.App) (.NET 10 MAUI, MVVM).

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet test tests/Core.Sync.Tests          # or any test project; Core.Sync has the multi-phone tests
```

The Android app needs the MAUI Android workload, JDK 17 and the Android SDK:

```sh
dotnet workload install maui-android
dotnet publish src/Apps/FamilyLists.App -f net10.0-android -c Release
```

The APK is written to `src/Apps/FamilyLists.App/bin/Release/net10.0-android/publish/`. For the x86-64 emulator, add `-p:EmulatorBuild=true`. Release signing reads `%USERPROFILE%\.familylists\signing.props` (never commit keys); see [docs/RELEASING.md](docs/RELEASING.md).

## Privacy

- Nothing leaves the phone unencrypted. The relay sees only random topic names and padded ciphertext.
- Bill photos stay on the phone that took them (encrypted); only the bill's lines and totals are shared with the group.
- The Claude reader is off unless you turn it on and enter your own key. When it is on, the bill photo is sent to Anthropic's API to be read.
- No analytics, no ads, no accounts.

## License

MIT. Third-party components and services: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
