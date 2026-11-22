# TD-01 Serverless sync

Adapted from Travel Vault's `Core.Sync` (same author, MIT), and made generic so the domain decides what is synced.

## Records
Every kind of synced record implements `ISyncedRecords` (`Core.Vault/SyncedRecords.cs`):
- a short type tag;
- `LoadAsync(id)`, which returns the record's group, JSON payload and version;
- `ApplyAsync(groupId, id, payload)`, which stores an incoming record if it is newer.

| Tag | Record | Handler |
|---|---|---|
| `fam` | Group: name, members, roles, currency, relay topic | `FamilySync` itself |
| `lst` | List (monthly or to-do) | `ListSync` |
| `mon` | A list's month | `ListSync` |
| `itm` | Item | `ListSync` |
| `bil` / `bln` | Confirmed bill and its lines | `BillSync` (drafts are never sent) |

Versions are hybrid logical clocks, and the last writer wins per record. A handler checks that an incoming record's parent belongs to the same group, so one group can never write into another's data.

## Messages
- **Outbox:** each local change is queued, keeping only the newest version of each record.
- **Sending:** a batch is signed (ECDSA P-256) with the phone's device key, then encrypted (AES-256-GCM) with the group key. It is padded to a fixed size bucket and posted as text, with prefix `fl2.`, to the group's random topic on ntfy.sh.
- **Receiving:** the receiver checks the signature against the member list, then the sender's message number against a sliding replay window (C-007): each number is accepted once, in any order, within the last 256. A plain "higher than the last one" rule isn't enough, because ntfy.sh can return two messages sent in the same second in either order. The receiver applies the group record first, then the rest.
- **Catch-up:** ntfy.sh keeps messages for about 12 hours. A phone that hasn't synced for more than 11 hours asks the others for the full state, and anyone online answers.

## Joining
- **In person:**
  - The admin's phone shows a QR code (`fl1:q:`) carrying the group key, the topic and a one-time secret, valid for 15 minutes.
  - The joiner proves it holds the secret, and the admin's phone admits it automatically.
- **Remote:**
  - An invite text (`fl1:r:`) carries only a join topic and a secret.
  - The joiner sends a join request (`flj1.`).
  - The admin compares the 6-digit safety code shown on both phones, then approves (`flj1ok.`) or declines (`flj1no.`).
  - On approval, the group key and the signed group record are compressed and sealed to the joiner's device key, so the approval fits in one relay message even for a big group (C-010). Approving again re-sends it if the first attempt never left the phone.
- **Removing or leaving:** when a member is removed or leaves, the group key and topic are rotated. The new key is sealed to each remaining member, and sent in as many messages as needed. Each message holds the signed group record and a share of the sealed keys, and a phone acts on the one with its key. The old topic is still read for 3 days, so messages already on their way aren't lost.

## When sync runs (app)
- **Live while the app is open.**
  - The app keeps a streaming `GET /<topic>/json` connection open (`NtfyRelay.WatchAsync`).
  - Each message triggers a sync of that group 1 s later, so a burst of messages becomes one sync. ntfy.sh announces a message on the stream slightly before a fetch can return it, so a fetch that finds nothing is tried once more 3 s later.
  - The stream uses its own `SocketsHttpHandler` client with no overall timeout. Android's default handler held the stream back in testing (C-006), and the shared client's 30 s timeout would cut it off.
  - ntfy sends a keepalive every 45 s. If nothing arrives for 100 s, the stream is dropped and reopened.
- **After a local change:** a send in the background, without waiting.
- **Every minute** while the app is open.
- **About every 15 minutes** in the background (JobScheduler, job id 6101).

## Known limits
- **Group size:** the signed group record (every member's name and public keys) must fit in one relay message. It is tested with 7 members, which fits comfortably; there is no hard limit in the app yet.
- **Relay:** ntfy.sh's free limits apply. A household's traffic is far below them.

## Tests
`tests/Core.Sync.Tests` runs several phones (`Phone.cs`: real encrypted SQLite, `InMemoryRelay`). It covers:
- in-person and remote joins with approval;
- ticks reaching the other phone;
- two phones starting the same month without duplicates;
- confirmed bills reaching the other phone;
- catch-up after two days away;
- a removed member getting nothing new;
- two groups staying isolated;
- two messages the relay returns in the wrong order both being applied, and each only once;
- concurrent edits resolving the same way on every phone.

`NtfyRelay` has its own tests against a fake HTTP handler. The real relay was tested with the emulator and a console "second phone" built on the same libraries.
