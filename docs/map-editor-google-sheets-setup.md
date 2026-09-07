# Map Editor — Google Sheets Connectivity Setup

Operator guide for wiring the map editor's Google Sheets sync (OAuth sign-in, the
desktop client JSON, spreadsheet sharing, tokens, and troubleshooting). No real
client IDs, secrets, or spreadsheet IDs appear in this document; substitute your own
wherever you see placeholders like `YOUR_CLIENT_ID`.

## 1. Google Cloud project and Sheets API

1. Create (or select) a project at <https://console.cloud.google.com>.
2. Enable the **Google Sheets API**: *APIs & Services → Library → Google Sheets API →
   Enable*. The sync feature does not work until the API is enabled on the project;
   requests to a disabled API fail with `403` / `PERMISSION_DENIED` naming the API.

## 2. OAuth consent configuration

Open *APIs & Services → OAuth consent screen* and complete the app configuration
(product name, support email, scopes — the editor requests the Sheets read/write
scope).

- **User type: Internal** — only accounts inside your organisation can sign in.
  Choose this for a private team; the app shows as "verified" to those accounts and
  no test users are needed.
- **User type: External** — publishing state matters:
  - **Testing** — the app shows an "unverified app" warning and *only* accounts you
    add as **test users** (*Audience → Test users*) can complete sign-in. Add every
    person who will sign in while you iterate.
  - **Production** — any Google account can sign in; the unverified-app warning
    still shows until Google verifies the app. The Sheets read/write scope is a
    sensitive scope under Google's OAuth classifications, so an External app in
    Production using it must complete Google's verification process.

## 3. Desktop app OAuth client

1. *APIs & Services → Credentials → Create credentials → OAuth client ID*.
2. Application type: **Desktop app**.
3. Download the JSON. It contains `client_id` (`YOUR_CLIENT_ID`), `client_secret`,
   `auth_uri`, and `token_uri`. This file is the desktop client configuration —
   keep it out of Git (see section 10).

## 4. Supplying the client JSON

The editor locates the client JSON in this order:

1. the absolute path in the `GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT` environment
   variable (used for local development), or
2. `google-oauth-client.json` beside the executable (used by released archives).

For development, export the variable pointing at the downloaded file:

```bash
export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT=/absolute/path/to/your-desktop-client.json
```

For release builds, `build-map-editor.sh` **requires** the same variable to be set to
an absolute path of an existing file. The script validates it as a desktop-client
JSON (required fields `client_id`, `client_secret`, `auth_uri`, `token_uri`; a `web`
client is rejected) *before* anything is published, so a bad configuration fails
fast and never overwrites previous releases. When valid, the script copies it into
every staged application **beside the executable** as `google-oauth-client.json`
and ships it inside each archive. End users therefore receive the one configured
desktop client with the app and never install a separate file.

## 5. Sharing the spreadsheet

Share the target spreadsheet **directly with each signing-in Google account**
(*Share → add person → Editor* if they will push edits). Then paste the spreadsheet's
normal URL into the editor:

```
https://docs.google.com/spreadsheets/d/YOUR_SPREADSHEET_ID
```

`/edit`, query strings, and fragments are accepted; the editor stores the canonical
form `https://docs.google.com/spreadsheets/d/YOUR_SPREADSHEET_ID`.

## 6. First sign-in, loopback, and scopes

- On first **Connect** the editor opens the default browser to the Google consent
  page. After you approve, the browser is redirected to a **loopback** address
  (`http://127.0.0.1:PORT/...`); the editor runs a tiny local HTTP listener on
  `127.0.0.1` to receive the code. If the redirect page reports a connection
  failure, check that a firewall or proxy is not blocking the loopback port, and
  complete the flow on the same machine as the editor.
- The requested scope is **Sheets read/write** (`https://www.googleapis.com/auth/spreadsheets`).
  Approve it so both pull and push work.
- The client ID/secret in the JSON are **desktop-app OAuth client identifiers, not
  service-account credentials**. There is no service account, no `client_email`, and
  no private key; access is always as the signed-in person, subject to the
  spreadsheet's sharing permissions.

## 7. Where settings and tokens live

The settings file and the sibling `google-tokens` directory are per-machine:

| Platform | Settings file | Token directory |
| --- | --- | --- |
| Windows | `%AppData%\Goose2MapEditor\settings.json` | `%AppData%\Goose2MapEditor\google-tokens` |
| macOS | `~/Library/Application Support/Goose2MapEditor/settings.json` | `~/Library/Application Support/Goose2MapEditor/google-tokens` |
| Linux | `$XDG_CONFIG_HOME/goose2-map-editor/settings.json` (or `~/.config/goose2-map-editor/settings.json`) | `$XDG_CONFIG_HOME/goose2-map-editor/google-tokens` (or `~/.config/goose2-map-editor/google-tokens`) |

The remembered spreadsheet URL is stored in the **settings file**; it is just the
canonical URL and contains no credentials. The **refresh tokens** live in the
`google-tokens` directory and are what keep the session connected between launches.
Deleting the remembered URL does not sign you out; deleting the token directory does.

## 8. Disconnect versus revoking access

- **Disconnect (in the editor)** deletes the *local* token files. The Google account
  keeps its consent grant and the app keeps its OAuth client; connecting again
  reuses or re-prompts for consent.
- **Google Account → Security → Third-party access** (or *Security → Third-party
  apps with account access*) revokes the grant on the *Google* side. After that, the
  stored refresh token stops working and the next Connect performs a full
  re-authorization.

Use Disconnect for "sign out on this machine"; use the Google-side revocation when
leaving the team or when a machine is compromised.

## 9. Troubleshooting

- **"Unverified app" / cannot sign in (External, Testing):** the account is not a
  configured test user. Add it under *Audience → Test users*, or switch the consent
  screen to Production/Internal.
- **Redirect/loopback failure:** the browser could not reach `127.0.0.1`. Run the
  editor and the browser on the same machine, allow the loopback port through any
  firewall, and avoid corporate proxies that intercept loopback traffic.
- **API disabled (`403 PERMISSION_DENIED` naming the Sheets API):** enable the
  Google Sheets API on the project (section 1).
- **`401` / invalid_grant:** the stored refresh token was revoked or expired. Run
  Connect again and re-authorize in the browser.
- **`403` on a specific spreadsheet (sharing/permission):** the signed-in account
  is not shared on the spreadsheet, or the scope is read-only. Share the sheet with
  that account (section 5) and confirm the Sheets read/write scope was granted.
- **`404` / notFound:** wrong spreadsheet URL or ID. Paste the normal
  `docs.google.com/spreadsheets/d/...` URL and confirm the sheet still exists.
- **Quota / rate-limit (`429`):** back off briefly; the editor's sync coordinator
  retries transient failures, but sustained `429`s mean the project quota is
  exhausted — check *APIs & Services → Credentials* usage and the Sheets API quota.
- **Schema/header errors:** the spreadsheet's layout no longer matches the expected
  game-data schema (wrong header row, renamed columns). Fix the sheet layout rather
  than the client; the error message names the offending sheet/column.

## 10. Secrets hygiene and live verification

- The **OAuth client JSON, the `google-tokens` directory, and machine-specific
  absolute paths must never be committed to Git**. The release script takes the
  client JSON from the release environment and packages it into the artifacts; it is
  not tracked in the repository.
- When verifying against live Google APIs, use a **disposable shared spreadsheet**
  (a throwaway project + sheet shared with a throwaway test account). Never point
  experiments at production game data, and revoke/delete the disposable project
  afterwards.

## 11. NPC preview — manual smoke path

Preview rendering is a read-only overlay: it never mutates rows, history, dirty state,
selection, or sync command enablement, and it degrades to the normal spawn marker when
appearance assets are unavailable. Verify each step below in a headed editor; after every
failure case, confirm the map is still fully editable and that **Save**, **Pull**, **Push**,
undo/redo, and the dirty indicator behave exactly as before the failure.

1. **Converter generation.** From `tools/AssetConverter/src/AssetConverter` run
   `dotnet run -- all` (or `dotnet run -- animations` to regenerate only the appearance
   sidecar). It writes `Assets/Sprites/appearance-manifest.json` next to `manifest.json`
   and the sheet PNGs.
2. **Asset-directory open.** Launch the editor and open the `Assets/Sprites` directory.
   Map tiles, palette, and previews all come from this one directory; a single shared
   appearance catalog is used by every open tab.
3. **Normal/preview toggle.** With the spawn tool, place two spawns on the same NPC.
   Toggle *Preview* in the menu. Normal mode draws the compact marker on each spawn tile;
   preview mode draws the composed NPC art at the same (marker) tile and the status bar
   reads `Art preview active`. Toggling back and forth must not change the rows, history,
   dirty state, or any sync command.
4. **Male/female underwear.** Spawn an NPC with body 1 (male) and one with body 11
   (female). The male shows legs part 3; the female shows legs part 4 and chest part 8.
5. **Monster.** Spawn an NPC with body 100; the monster body frame renders in its place.
6. **Tinted equipment.** Spawn an NPC with a non-zero body tint and an equipped item with
   its own tint. The tint blends into the part pixels (it is a color tint, not opacity):
   transparent pixels stay transparent and the source alpha is preserved.
7. **Overlap selection.** Place two spawns on the same tile (or on overlapping art) and
   switch to preview mode. Clicking selects the spawn occurrence by its anchor/marker
   tile identity, not by sprite pixels, so the preview art does not interfere with
   selection. When multiple spawns share a tile, clicking always selects the first
   occurrence at that tile; drag the first marker to a free tile and the second
   occurrence's anchor becomes clickable in its place.
8. **Layer-2 crossing / Y order.** Put a layer-2 object and an NPC on crossing tiles. The
   entity stage Y-sorts by bottom-center anchor, so the lower NPC draws over the object
   and the higher one behind it; moving the NPC's marker across the object's row flips
   the order.
9. **Missing sidecar.** Delete `appearance-manifest.json` and reopen the directory.
   Preview mode falls back to the normal markers (no anchors, no art), the preference
   stays checked, and the status bar shows `Art preview unavailable: …` nonmodally.
   Map tiles and all editing keep working; nothing is marked dirty by the fallback.
10. **Malformed sidecar.** Write garbage (or a wrong `version`) into
    `appearance-manifest.json` and reopen. Same fallback as the missing sidecar, with the
    parse diagnostic in the status text; the map manifest and sheet PNGs remain fully
    usable.
11. **Missing part.** Point an NPC at a part id absent from the sidecar (e.g. an unknown
    hair id). The remaining parts still render, the missing slot shows a placeholder box,
    and the spawn anchor is present and clickable.
12. **Missing PNG.** Delete the sheet PNG a part references and reopen. That part shows a
    placeholder; the other parts and the anchor render and select normally.
13. **Save while previews fail.** With the sidecar missing or malformed, save the map.
    The save succeeds, the dirty indicator clears, and the preview fallback state is
    unchanged.
14. **Restoration.** Reopen the directory with a valid sidecar and PNGs. Preview mode
    immediately renders the composed art again (the preference was never cleared), and
    the status returns to `Art preview active`.
