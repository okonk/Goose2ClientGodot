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
    still shows until Google verifies the app (verification is only required for
    sensitive scopes, which the Sheets scope is not, but the warning remains).

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
