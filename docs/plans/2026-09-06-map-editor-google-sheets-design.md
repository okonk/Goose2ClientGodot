# Map Editor Google Sheets Integration Design

Date: 2026-09-06
Branch: `map-editor-google-sheets`

## Goal

Let a small trusted team pull, edit, preview, and push NPC spawns and warps from the standalone map editor while Google Sheets remains the source of truth. Each user authenticates with their own Google account and must already have access to the chosen spreadsheet.

The integration uses explicit Pull and Push operations. Saving a binary map never implicitly writes sheet data.

## Scope

The initial release supports:

- Pasting and remembering a game-data spreadsheet URL
- Google OAuth through each team member's account
- Confirming the opened map's `Maps` row
- Pulling that map's NPC spawns and warps
- Editing spawns and warps directly on the canvas and in the right-side property panel
- Static down-facing NPC previews at spawn locations
- Replacing the map's complete remote spawn and warp sets on Push
- Warning before overwriting remotely changed data
- Independent dirty state and undo for sheet data

Deferred:

- A global spawn/warp list or dialog
- Special handling for overlapping spawns
- Animated NPC previews
- Automatic merge of concurrent edits
- Automatic updates to inbound warps when a destination map is resized
- Implicit synchronization during map Save

## Architecture

Add `MapEditor.GameData`, a provider-neutral project containing:

- `WarpRow`, `NpcSpawnRow`, `MapReference`, and `NpcAppearance` data records
- Spreadsheet URL and ID parsing
- Generated schema loading and row mapping
- Pull snapshots and order-independent multiset comparison
- Replacement-set planning and validation
- Sheet-data edit commands and history
- A game-data gateway abstraction
- A Google Sheets implementation using the official .NET API

`MapEditor.Core` remains map-only and receives no Google dependency. `MapEditor.App` owns OAuth interactions, UI state, and the game-data state attached to each open map tab. `MapEditor.Rendering` receives provider-neutral NPC appearance and overlay inputs.

Sheet data is held in memory and is never serialized into the binary map. Closing a tab with unpushed changes requires Push or Discard, separately from the existing unsaved-map prompt.

## Shared spreadsheet schema

`CsvToSql.Core.Schema.SchemaRegistry` in the server repository remains the authoritative worksheet contract. The map editor must not duplicate worksheet names, positional column indexes, SQL ranges, or references by hand.

Extend the server's `SchemaGen` to emit a neutral `game-data-schema.json` alongside `schema.js`. The map editor checks in and embeds the generated JSON. It uses descriptor names to locate the fields consumed from:

- `Maps`
- `NPCs`
- `NPC Spawns`
- `Warptiles`

The editor still uses typed row records, but those represent values rather than schema metadata. One mapper converts positional Sheet cells into those records using the generated descriptors.

Because the importer reads rows positionally, consumed descriptors gain their expected worksheet header text. The generated artifact includes those headers, and Pull verifies them before interpreting data. A stale or reordered schema therefore fails safely.

If the repositories later become a monorepo, the descriptor types and registry can move into a lightweight `GameData.Schema` project referenced directly by `CsvToSql.Core`, `SchemaGen`, and `MapEditor.GameData`. This replaces the generated-copy boundary without changing the editor's records, UI, or gateway.

## Authentication and spreadsheet selection

Use Google's installed-application OAuth flow with Sheets scope:

1. The user chooses Connect to Google.
2. The editor opens the system browser for sign-in and consent.
3. Google redirects to a temporary loopback listener.
4. API calls execute as the signed-in user and use normal spreadsheet sharing permissions.
5. Disconnect removes the locally cached authorization.

The release ships one desktop OAuth client configuration. OAuth client IDs are not secrets; service-account keys and shared team credentials are not used.

Refresh tokens use the standard Google client token store under the user's private application-data directory. The remembered spreadsheet URL is stored independently in existing map-editor settings. Users may paste a different URL at any time, and no map-to-row association is persisted.

A committed `docs/map-editor-google-sheets-setup.md` guide covers:

- Creating or selecting a Google Cloud project
- Enabling the Google Sheets API
- Configuring the OAuth consent screen
- Internal, testing, and production access choices
- Adding trusted test users where applicable
- Creating and supplying a desktop OAuth client configuration
- First sign-in and spreadsheet sharing requirements
- Local token and remembered-URL storage
- Disconnecting and revoking access
- Common unverified-app, redirect, and permission errors

Credentials, refresh tokens, and machine-specific configuration are never committed.

## Per-document state

Each map tab independently tracks:

- Confirmed spreadsheet ID and map ID
- Last successfully pulled remote spawn and warp snapshots
- Editable local spawn and warp sets
- Referenced maps and NPC appearance data
- Sheet-data dirty state
- Sheet-data undo and redo history
- Data overlay and NPC preview visibility

The Google connection and remembered spreadsheet URL are application-wide.

## Pull workflow

1. The user pastes or accepts the remembered spreadsheet URL.
2. The editor loads `Maps` through the generated schema.
3. It preselects the row whose `map_filename` matches the open map filename.
4. The user confirms the suggested row or selects another through an ID/name search. The choice is not remembered.
5. A batched request loads `NPC Spawns`, `Warptiles`, and the NPC fields needed for names and appearance.
6. Rows belonging to the confirmed map become both the remote snapshot and the local working set.

An untitled or unmatched map receives no automatic selection. Pull does not replace local state until all required reads, schema checks, and row validation succeed. Pulling again while local sheet data is dirty requires Push or Discard.

## Push workflow

The local spawn and warp sets completely replace the current map's corresponding remote sets.

Before writing, the editor re-reads rows for the current map and compares them with the pull snapshot as order-independent multisets. Duplicate rows therefore retain their multiplicity.

- If the remote data is unchanged, Push proceeds.
- If it changed, the editor offers Overwrite, Pull Instead, and Cancel.
- Overwrite compares the local set against the latest remote set and changes only rows belonging to the confirmed map.
- Unmatched remote rows are deleted in descending row order and unmatched local rows are appended.
- One Sheets `batchUpdate` applies the replacement atomically.
- Rows belonging to other maps are never rewritten.
- Success promotes the local set to the new snapshot and clears sheet-data dirty state.

Google Sheets does not provide an atomic compare-and-replace spanning the conflict read and write. A concurrent edit in that narrow interval can still be overwritten. This best-effort limitation is accepted and documented.

An ambiguous result caused by losing the connection during Push requires Pull before another Push attempt.

## Editing UI

Add a Game Data menu and a compact toolbar group with:

- Connect or Disconnect Google
- Pull
- Push
- Toggle data overlays
- Toggle NPC preview mode

No spawn/warp list or dialog is included. Entries are edited through canvas tools and the existing right sidebar.

### Spawn tool

- The sidebar offers a searchable NPC ID/name picker.
- Clicking an empty tile places a spawn using the chosen NPC.
- Clicking an existing spawn selects it.
- The sidebar shows NPC and coordinates and offers Delete.
- Dragging moves the selected spawn.
- Multiple spawns on one tile remain valid, but clicking selects the first matching spawn and there is no special overlap UI.

### Warp tool

- Clicking a tile creates or selects its warp.
- The sidebar shows a searchable destination map picker and destination coordinates.
- Use Selected Tile copies the selected coordinate from an open tab associated with the destination map.
- Dragging moves the warp source.
- Delete removes the selected warp.
- Warp overlays use markers rather than cross-map arrows.

The right panel remains unchanged for ordinary map tools. It switches to spawn or warp properties when the corresponding tool or entry is active.

## Clipboard and undo

Rectangular map selection copy, cut, and paste remain tile-only and never include spawns or warps.

When an individual game-data entry is selected:

- Copying a spawn records its NPC ID.
- Pasting creates a new spawn at the selected tile.
- Copying a warp records its destination map and coordinates.
- Pasting creates a new warp at the selected source tile.
- Cut removes the local source entry and makes it available for paste.
- Cross-tab paste requires both tabs to have pulled from the same spreadsheet.
- Paste is rejected when the destination tab has no pulled game data.

Sheet-data edits have their own undo stack. Ctrl+Z and Ctrl+Y act on the domain—map or sheet data—edited most recently. Push advances only the sheet-data baseline and does not affect map-file history.

## Map resizing

When a map with pulled data is resized:

- Locally loaded spawn and warp source coordinates move with the map content.
- Entries cropped outside the new bounds are shown in a confirmation before removal.
- Self-warp destinations move by the same transform.
- The editor warns that inbound warps from other maps are not loaded and cannot be updated automatically.

Inbound warp maintenance remains outside the initial scope.

## NPC preview assets

NPC previews require a semantic part mapping such as `Bodies/12` to sprite-sheet frames. The current `manifest.json` only maps `(sheet, graphic)`, so the map editor will not independently parse Godot `.tres` files.

Extend `AssetConverter` to emit `appearance-manifest.json` containing the static down-facing frame for each body, hair, eyes, chest, helm, legs, feet, hand, and weapon or shield part. The map editor loads it beside `manifest.json`.

A missing or invalid appearance manifest disables NPC art previews but does not disable synchronization, editing, or normal spawn markers. Missing individual parts render diagnostic placeholders without hiding valid parts.

The generated game-data schema identifies the NPC columns needed to build `NpcAppearance`, including body, face, hair, tints, body state, and `equipped_items`.

Composition follows client rules:

- `body_id >= 100` renders only the monster body.
- Humanoids render body, underwear fallbacks, face, hair, and equipment in client order.
- Tint alpha is a blend factor, not ordinary sprite opacity.
- The initial preview is static and down-facing.

## Rendering order

Rendering is staged as:

1. Flat map layers below entities
2. Map layer 2 objects and NPC previews, sorted by bottom-center Y
3. Flat layers above entities
4. Editing markers, selections, and tool overlays

Normal marker mode draws a compact spawn marker above map art. Preview mode substitutes the composed character but retains a selectable outline or anchor so transparent or occluded NPCs remain editable.

## Validation

Push is blocked when:

- A spawn references an NPC absent from pulled NPC data.
- A spawn or warp source lies outside the current map.
- More than one warp occupies the same source tile.
- A destination map does not exist.
- A destination coordinate is negative or exceeds the descriptor's numeric range.
- A destination map is open and the coordinate lies outside its known dimensions.
- Required worksheets or consumed columns are absent or reordered.

The `Maps` sheet does not store map dimensions. A closed destination map therefore receives ID and numeric-range validation but only a warning about bounds that cannot be checked.

## Failure handling

- Authentication expiry requests reauthorization without discarding local edits.
- Permission and malformed-URL errors identify the affected spreadsheet.
- Rate limits and temporary network failures receive bounded retries with backoff.
- Pull retains the previous local state unless the complete operation succeeds.
- Push retains sheet-data dirty state until Google confirms the batch.
- Schema mismatch blocks Pull and Push.
- Disconnecting or closing with dirty sheet data requires Push or Discard.
- Map saving remains available when Google or NPC preview assets are unavailable.

## Testing

Automated tests cover:

- Spreadsheet URL parsing and remembered selection
- Generated-schema loading and worksheet header verification
- Positional row mapping
- Order-independent snapshots, including duplicate rows
- Replacement-set batch generation preserving other maps
- Remote-change warning and overwrite behavior
- IDs, numeric ranges, source bounds, duplicate warp sources, and open-destination bounds
- Separate and coordinated map and sheet-data histories
- Individual spawn and warp clipboard behavior
- Resize transforms, cropping, and self-warp destinations
- NPC appearance composition, client tint blending, and monster bodies
- Entity and map-object Y ordering
- Missing appearance assets and partially renderable NPCs
- Application state through mocked gateway responses

A manual integration pass uses a disposable spreadsheet shared with test accounts to verify OAuth, permissions, real batch updates, conflict warnings, reconnect behavior, and access revocation. Automated tests never require live Google credentials.
