# Invisibility (Client) — Design

Client-side rendering of invisible characters. The server is not yet implementing the
feature, but the wire formats are known and the client has everything it needs.

## Wire format

- **MKC / CHP** already carry an `Invisible` field for normal characters (0 = visible,
  1 = invisible). It is parsed today but unused.
- **Monster** MKC/CHP variants: the field currently skipped as a string
  (`p.GetString(); // invisible`) is actually an int — change to
  `packet.Invisible = p.GetInt32();` in both packet parsers so all characters carry the
  field uniformly.
- **SINVS** (new): `SINVS<value>` where value is 1 if the viewer can see invisible
  characters, 0 if not. The server sends it after map load.

## Rules

Let `H` = character is invisible to the viewer.

- `H` → render nothing: sprite, name label, HP/MP bars, chat bubble, battle text, emotes,
  and spell impact FX. Movement/vitals simulation continues unchanged.
- Invisible but visible to the viewer (SINVS = 1, or the character is your own) → render
  everything, with the body **sprite slots only** at 50% alpha (`Modulate` alpha 0.5).
  Overlays (name, bars, bubbles, text) stay fully opaque.
- Otherwise → render normally.
- Your own character, when invisible, is always rendered translucent — even with SINVS = 0.
- Invisible characters still occupy and block their tile (existing `IsValidMove`
  occupancy check, unchanged — hidden characters keep simulating positions).
- Invisible-to-viewer characters are clickable for movement/click purposes (existing
  `ContainsPoint` behavior, unchanged) but are **not targetable with spells**.
- No client-side GM/admin bypass; the server owns that.
- `CanSeeInvisible` defaults to false until the first SINVS arrives.

## Components

### 1. `SeeInvisiblePacket` (new, `Scripts/Network/Packets/SeeInvisiblePacket.cs`)

`PacketHandler` with prefix `SINVS`; parses a single int into a bool property
(1 = can see invisible). Follows the existing packet pattern.

### 2. `GameManager`

- `public bool CanSeeInvisible { get; set; }` (default false), set by the SINVS handler
  in `MapManager`.

### 3. `Character`

- `public bool IsInvisible { get; private set; }` — set from the packet field in both
  `SetAppearance` overloads.
- `public bool IsHiddenFromViewer { get; private set; }` — the evaluated "hidden" state;
  consulted by `WorldTextBridge` and `SpellTargetManager`.
- `static InvisibilityRule Evaluate(bool isInvisible, bool canSeeInvisible, bool isLocalPlayer)`
  — pure decision function returning `Normal / Translucent / Hidden`. Extracted for
  headless unit testing:
  - `Hidden` iff `isInvisible && !isLocalPlayer && !canSeeInvisible`
  - `Translucent` iff `isInvisible` (and not `Hidden`)
  - else `Normal`
- `void ApplyInvisibility()` — recomputes via `Evaluate` from
  `(IsInvisible, GameManager.Instance?.CanSeeInvisible ?? false, IsLocalPlayer)`:
  - `Hidden` → `IsHiddenFromViewer = true`; set this node's `Visible = false` (kills slot
    sprites, HP/MP bars, emotes, spell FX — all direct children).
  - `Translucent` → `IsHiddenFromViewer = false`; node visible; set `Modulate =
    (1, 1, 1, 0.5)` on every slot sprite in `_slots`.
  - `Normal` → node visible; slot `Modulate` alpha back to 1.
  - If `IsHiddenFromViewer` flipped and the character is the current spell target, notify
    the SpellTargetManager (via `GameManager`) to reset the target to the local player.

### 4. `WorldTextBridge`

In `UpdateProjection`, after the anchor-validity checks, an element whose
`AnchorOwner` (a `Character.Character`) reports `IsHiddenFromViewer` gets
`item.Visible = false; continue;`. The bridge only *consults* the state; the rule lives
in `Character.Evaluate`.

### 5. `MapManager`

- Register `SeeInvisiblePacket` in `_Ready` / remove in `_ExitTree` alongside the other
  character listeners. (SINVS arrives after map load, so the per-map listener is safe.)
- Handler: set `GameManager.Instance.CanSeeInvisible`, then call
  `ApplyInvisibility()` on every character in `_characters`.

### 6. `SpellTargetManager`

- `CycleTarget`: build candidates only from characters where
  `!c.IsHiddenFromViewer`.
- `Cast` (remembered-target validity): discard the remembered target and fall back to the
  local player if it reports `IsHiddenFromViewer`.
- Expose the reset used by `Character.ApplyInvisibility` when a character becomes hidden
  while it is the current spell target.

## Triggers for `ApplyInvisibility()`

1. MKC — end of `SetAppearance(MakeCharacterPacket)`.
2. CHP — end of `SetAppearance(UpdateCharacterPacket)`.
3. SINVS — `MapManager` handler loops all characters.
4. Local-player attach — `AttachLocalPlayer`, after `IsLocalPlayer` is set (the "self"
   exception; this flag flips after the MKC's own `SetAppearance`).

## Tests (headless, `tests/Goose2Client.Tests`)

- `SeeInvisiblePacket` parse: `SINVS1` → true, `SINVS0` → false (existing packet-test
  pattern).
- `Character.Evaluate`: truth table over (isInvisible, canSeeInvisible, isLocalPlayer) —
  8 combinations, no Node2D instantiation.
- MKC/CHP parse: monster branch now reads `Invisible` as an int (guards the wire-format
  change).

## Deferred (out of scope)

- GM/admin seeing invisible characters — server-side concern.
- Click-selecting invisible characters for movement clicks — accepted behavior.
