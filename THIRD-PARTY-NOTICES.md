# Third-Party Notices

Everything Family Lists uses is free to use. This list is checked at every release.

## Shipped in the app

| Dependency | Used by | License |
|---|---|---|
| .NET runtime and libraries | All | MIT |
| .NET MAUI (Microsoft.Maui.Controls) | App | MIT |
| CommunityToolkit.Mvvm | App | MIT |
| QRCoder (invite QR codes) | App | MIT |
| Microsoft.Data.Sqlite.Core | Storage.Sqlite | MIT |
| SQLitePCLRaw (core, provider, `bundle_e_sqlite3mc`) | Storage.Sqlite | Apache-2.0 |
| SQLite3 Multiple Ciphers (native encrypted SQLite, inside `lib.e_sqlite3mc`) | Storage.Sqlite | MIT; SQLite itself is public domain |
| Anthropic C# SDK (`Anthropic`) | Core.ClaudeReader (optional bill reader) | Apache-2.0 |
| Microsoft.Extensions.AI.Abstractions (used by the Anthropic SDK) | Core.ClaudeReader | MIT |
| Open Sans font | App | SIL Open Font License 1.1 |
| Google ML Kit text recognition and barcode scanning (on-device models) | Device.Vision | Free to use under the [ML Kit Terms](https://developers.google.com/ml-kit/terms); closed source |
| Google Play services client libraries (used by ML Kit) | Device.Vision | [Android SDK License](https://developer.android.com/studio/terms) |
| .NET bindings for ML Kit (Xamarin.Google.MLKit.*) | Device.Vision | MIT |
| AndroidX bindings (Xamarin.AndroidX.*, through MAUI) | App | MIT (bindings), Apache-2.0 (AndroidX) |
| Code adapted from *Travel Vault* and *Use It Before*: sync, relay, security, storage, updater, background work, photo shrinking, styles | Several | MIT (same author) |

The ML Kit bindings are pinned (`Xamarin.Google.MLKit.*`): newer versions need AndroidX libraries newer than .NET MAUI 10 supports, and fail to restore (NU1608).

## Services

| Service | Used for | Terms |
|---|---|---|
| [ntfy.sh](https://ntfy.sh), the public instance of the open-source ntfy server | Carrying end-to-end encrypted group changes between phones. Messages are kept about 12 hours. No account. | Free public service, used within its [terms](https://ntfy.sh/docs/privacy/) and limits |
| [Anthropic API](https://www.anthropic.com/legal/commercial-terms) | Only if the person turns on the Claude bill reader and enters their own API key: the bill photo is sent to be read, and billed to their own Anthropic account | Anthropic's terms for API customers |
| GitHub Releases | Distributing the APK and checking for updates | Free |

## Development and test only (not shipped)

| Dependency | License |
|---|---|
| xUnit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
