# Name Display Options — Design

Adds a client setting for how character names are shown, plus a server-driven per-character
override. Spans two repos: the Godot client (`Goose2ClientGodot`) and the server
(`illutiagooseserver`).

## Behaviour

Three client modes for overhead (above-the-head) names, chosen in the Options window:

- **Always** — every visible character's name renders overhead (today's behaviour).
- **Players only** — only `CharacterType.Player` renders overhead. Default.
- **Never** — no overhead names.

The alternative to overhead rendering is a **hover tooltip**: mousing over a character shows
its name in a tooltip. The tooltip is the *fallback* — it appears for a character only when
that character's name is **not** being drawn overhead in the current mode. So in "Always"
mode the tooltip never appears (except for server-hidden names, below); in "Never" mode every
hoverable character tooltips.

The server can flag a character to **force its name off the overhead display**. This is
overhead-only: a force-hidden character still reveals its name via the hover tooltip,
regardless of the client mode.

### Decision rules

Two pure functions (headless-testable), mirroring the existing `InvisibilityRule`:

```
ShouldRenderNameOverhead(mode, type, serverHidden):
    serverHidden -> false
    Always       -> true
    PlayersOnly  -> type == CharacterType.Player
    Never        -> false

ShouldShowNameTooltip(visibleToViewer, roofOccluded, overheadShown):
    visibleToViewer && !roofOccluded && !overheadShown
```

`overheadShown` is `ShouldRenderNameOverhead(...)`, so a server-hidden character (whose
overhead is suppressed) still tooltips. A character hidden from the viewer (invisible) or
covered by a visible roof band shows neither overhead name nor tooltip.

## Wire format

`HideName` rides on **MKC** (the character-spawn packet) as a new field **immediately after the
existing `Invisible` field**, read explicitly in both the layered and monster parse branches. It
is static template data fixed at spawn, so **CHP is unchanged** and the client's CHP parser is
untouched.

A *trailing* field was considered and rejected: the client's mount parser consumes only one token
for a zero-id mount (`MakeCharacterPacket.cs:117`) while the server emits `"0,*"` (players) or
`"0,0,0,0,0"` (NPCs), leaving unconsumed trailing tokens — a field appended at the end would be
mis-read. Placing `HideName` right after `Invisible` (a position both sides read by name) avoids
that and keeps every later field aligned by symmetric insertion.

- Server emits `...,<invis>,<hideName>,...` (0/1) in every MKC builder. Players and pets emit `0`;
  NPCs emit their `hide_name` value.
- Client reads `packet.HideName = p.GetInt32() != 0;` right after `packet.Invisible = p.GetInt32();`
  in both the layered and monster branches.

**Deployment:** because the field is positional (mid-packet), client and server must deploy
together — a new client against an old server (no field) would mis-parse subsequent fields. This
is an accepted, coordinated change.

The value is only ever set from NPC data today (see Server section); the player/pet `0` keeps the
wire uniform and leaves room for a future per-player source.

## Client components

### 1. Setting

- `enum NameDisplayMode { Always = 0, PlayersOnly = 1, Never = 2 }` (`Scripts/Constants.cs`).
- `Options.NameDisplay` key (`Scripts/Constants.cs`); stored as an int in
  `CharacterSettings.Options`; default `PlayersOnly`.
- `GameManager.NameDisplay` (cached `NameDisplayMode`), read from settings at login
  (`GameManager.LoadCharacterSettings`) and updated live when the dropdown changes. Cached so
  the per-frame bridge check is a field read, not a dictionary/JSON lookup.

### 2. Options window

- 3-item `OptionButton` labelled "Name Display" in `Scenes/UI/OptionsWindow.tscn`, wired in
  `Scripts/UI/OptionsWindow.cs` like the other options: seed `Selected` from the setting,
  on `ItemSelected` write `Options.NameDisplay`, `Save()`, and set `GameManager.NameDisplay`.
  The window grows by one row.

### 3. Character

- `public bool NameHiddenByServer { get; private set; }` — set from the MKC trailing field.
- `public bool ShouldShowNameOverhead` — `ShouldRenderNameOverhead(GameManager.NameDisplay,
  CharacterType, NameHiddenByServer)`.
- The pure rule lives in a small static helper (e.g. `Scripts/Character/NameDisplayRule.cs`)
  for testing.

### 4. WorldTextBridge

In `UpdateProjection`, alongside the existing hidden/roof checks, hide the element when it is a
`BridgedNameLabel` and `!owner.ShouldShowNameOverhead`. Only the name label is gated — chat
bubbles and battle text are unaffected. Because the bridge re-evaluates every frame, flipping
the dropdown updates overhead names instantly with no explicit refresh.

### 5. Hover tooltip

- New `NameTooltipControl` (`Scripts/UI/`), modeled on `TextTooltipControl` /
  `MapItemTooltipControl`: a single mouse-anchored label with a settable text + color. Added to
  the Tooltips scene and a `ShowNameTooltip(text, color)` / `HideNameTooltip()` pair on
  `TooltipManager`.
- Hover detection is centralized in `MapManager`, driven from `WorldViewport._Input` (which
  already gates motion to the display rect and non-occluded area, alongside the existing click
  dispatch). On motion it converts to world coords and runs the existing `CharacterAt` hit-test;
  on leaving the display / over a window it hides the tooltip. No per-character physics nodes;
  moving characters work for free.
- The hovered character shows the tooltip iff `ShouldShowNameTooltip(...)`. Text is `FullName`;
  color is `GameColors.Blue` when the character `IsGM`, else the default tooltip color.
- **Stacking:** when the map-item tooltip is also visible at the same hover, the name tooltip
  positions itself directly below it (`y = itemTooltip.bottom + gap`) instead of at the cursor,
  so both read without overlapping. The existing bottom-of-screen clamp still applies.
- Pure `NameTooltipRule` (the `ShouldShowNameTooltip` function above) for headless testing.

### 6. Packet parse

`Scripts/Network/Packets/MakeCharacterPacket.cs`: add `packet.HideName = p.GetInt32() != 0;`
immediately after `packet.Invisible = p.GetInt32();` in both the layered and monster branches.
`Character.SetAppearance(MakeCharacterPacket)` sets `NameHiddenByServer` from it.

## Server components (`illutiagooseserver`)

### 1. NPC data field `hide_name`

Follows the `stationary` / `see_invisible` precedent exactly:

- `CsvToSql/CsvToSql.Core/NpcCsvToSql.cs`: append
  `Col.Bool("hide_name", def: false).HeaderText("hide name (0)")` at the **end** of the
  descriptor list (cells are read positionally; inserting mid-list would shift later columns).
- `Goose/NPCTemplate.cs`: `public bool HideName { get; set; }` + copy in the clone ctor.
- `Goose/NPCHandler.cs` `LoadNPCTemplates`: `npc.HideName = reader.GetString("hide_name") == "1";`.
- `Goose/NPC.cs`: `public bool HideName { get; set; }`, set per-instance like `CanMove` —
  `this.HideName = this.Properties.GetProperty("hideName", template.HideName);` — so a spawn can
  also override it via its properties dictionary.

The `npc_templates` DDL is generated from the descriptors (`TableDdl.Emit`) and NPC data is
regenerated from CSV each run, so no `ALTER TABLE` migration is needed. The source Google Sheet
"NPCs" tab needs the column appended for designers to set it; a blank cell defaults to `false`.

### 2. Packet emission

`Goose/Packets.cs`: insert the `HideName` field immediately after the `Invisible` field in
`MakeCharacter`, `MakeNPCCharacter`, and `MakePetCharacter` (players/pets emit `0`).
`UpdateCharacter` / `UpdateNPC` (CHP) unchanged.

## Tests

Client (`tests/Goose2Client.Tests`):

- `NameDisplayRule` truth table over (mode, type, serverHidden) — 3 × {Player, non-Player} × 2.
- `NameTooltipRule` truth table over (visibleToViewer, roofOccluded, overheadShown).
- `MakeCharacterPacket` parse: with trailing `HideName` = 1 / 0, and without the field
  (older server) → `false`, in both layered and monster branches.

Server (`Goose.Tests` / integration):

- `NpcCsvToSql` emits a `hide_name` column defaulting false.
- MKC builders append the trailing field (players/pets `0`, NPC its value).

## Notes / deferred

- **Default change:** existing users move from always-on to "Players only" on first load. This
  is intended; no per-user migration of the old behaviour.
- **Runtime mode change while hovering:** overhead names update instantly (bridge re-checks each
  frame); the tooltip refreshes on the next mouse motion. Negligible.
- **Name labels in "Never" mode** are still created and registered, just hidden by the bridge
  each frame, so toggling back needs no re-registration.
- **GM colour** is applied to the tooltip text (blue), matching the overhead label.
