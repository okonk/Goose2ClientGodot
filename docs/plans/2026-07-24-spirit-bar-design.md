# Spirit Bar in Vitals Window — Design

Date: 2026-07-24
Status: approved (revised 2026-07-24: SP panel as separate outline, original skin untouched)

## Summary

Add an optional **Spirit (SP) bar** to the Vitals window (`VitalsWindow.tscn`), below the
existing HP/MP bars. The bar is toggleable from the Options window, defaults to hidden
when the character has no SP (MaxSP == 0), and latches permanently visible (persisted
per character) once the server first reports MaxSP > 0.

SP is **local-player only** — no overhead bars, no party UI, no protocol changes.
`StatusInfoPacket` (SNF) already carries `MaxSP`/`CurrentSP`.

## Constraints (from user)

- The existing HP/MP layout must stay pixel-identical; when the SP bar is hidden, the
  window must look **exactly** like today (reviewer finding M2 → approach B chosen).
- The SP bar is a plain **rectangle** (flat left and right edges).
- SP bar left edge starts at the same x as the MP bar's left edge (window x49).
- SP bar right edge ends where the MP bar's 45° bevel meets its bottom edge (window x141).
- Spacing between MP and SP = spacing between HP and MP: no gap except the 1px black
  outline line between panels.
- Bar height: 2–4px less tall than the MP bar (17px) → **14px**.
- Colour: green/yellow, not pure yellow (too bright).
- Instead of a second full-window background: a **dedicated small SP outline texture**
  composited onto the original (reviewer round 2, user-approved).

## Approach

`Assets/UI/vitals-outline.png` (183×55) is **untouched**. A new 96×16 texture
(`vitals-sp-outline.png`) contains the SP panel — including the top black line that
extends the MP panel's existing bottom line — and is placed at window (47,45). Godot
controls don't clip children, so the SP bar and panel draw below the 55px window frame
with **no resizing of the root or Background** and no runtime texture swapping of the
main skin. When hidden, the three SP nodes are simply invisible → the window renders
byte-identically to today.

## Assets

### `Assets/UI/vitals-sp-bar.png` (new, 93×14, RGBA)

Plain rectangle, window coords x49–141 (93px wide), 14px tall. Three-tone vertical
striping matching the MP bar texture's scheme:

| Row | Colour | RGB |
|-----|--------|-----|
| y0 (top) | highlight | (190, 190, 90) |
| y1–y12 | body | (125, 125, 35) |
| y13 (bottom) | dark | (82, 82, 23) |

No bevels, no transparent margins — the shape fills the texture edge to edge.

### `Assets/UI/vitals-sp-outline.png` (new, 96×16, RGBA)

The SP panel, positioned at window (47,45), so texture (tx,ty) = window (x47+tx, y45+ty):

- row ty0 (y45): **full-width black line** (x47–142) — overwrites the MP bottom line's
  x47–140 identically and extends it to x142.
- rows ty1–ty14 (y46–y59): transparent at tx0; black border at tx1 (x48) and tx95 (x142);
  fill tx2–tx94 (x49–141): ty1 = (59,59,59), ty2–ty13 = (96,96,96), ty14 = (166,166,166)
  — same 3-shade track treatment as the HP/MP panels (border 1px left of the bar edge,
  matching the MP panel's own pattern at row y44).
- row ty15 (y60): transparent at tx0; black tx1–tx95 (x48–142).

### `Assets/UI/vitals-outline.png` — unchanged.

### Scene geometry (`Scenes/UI/VitalsWindow.tscn`)

| Node | Change |
|------|--------|
| root `VitalsWindow`, `Background`, all existing nodes | **unchanged** |
| `SpOutline` (new TextureRect) | x47–143, y45–61 (96×16), texture = vitals-sp-outline.png, `mouse_filter` = 2 (Ignore), `visible` = false. **All three SP nodes are inserted after `MpText`, before `Portrait`** — child order = draw order, and the level circle (x37–55, y36–55) overlaps the panel's x47–55 corner and must render above the SP nodes, exactly as it renders above the MP panel today |
| `SpBar` (new TextureProgressBar) | x49–142, y46–60 (93×14), `texture_progress` = vitals-sp-bar.png, `fill_mode` = 0, `mouse_filter` = 0, `max_value` = 1, `visible` = false |
| `SpText` (new Label) | mirrors `MpText` (mouse_filter=1, left-aligned, bottom-v-aligned) at x59–152, y46–60, `visible` = false; shows the current value only (the `x / y` total lives in the tooltip) |

## Behaviour (`Scripts/UI/VitalsWindow.cs`)

- On `SNF` (`StatusInfoPacket`): set `_spBar.Value = MaxSP == 0 ? 0 : CurrentSP/MaxSP`,
  `SpText` to `CurrentSP.ToString("N0")`, tooltip to `Spirit: {CurrentSP:N0} / {MaxSP:N0}`
  (same pattern as HP/MP, including hover wiring on bar + label). Mark `_snfReceived`.
- **Visibility** — evaluated every `_Process` (cheap direct option read, same
  read-on-demand pattern as `SpellTargetManager` for Target Filtering):

  ```
  shown = _snfReceived
        && ShowSpiritBar option (default true)
        && (SpiritBarShown latch  ||  MaxSP > 0)
  ```

  The whole rule lives in one pure function —
  `SpiritBarVisibility.ShouldShow(bool snfReceived, bool optionOn, bool latched, long maxSp)`
  — so every branch (incl. the pre-SNF gate) is unit-testable.

  - Hidden before the first SNF (no ghost bar for latched characters).
  - When `MaxSP > 0`: set latch → save once; bar shows.
  - When `MaxSP == 0` and never latched: hidden.
  - Latch persists per character in `CharacterSettings.Options["SpiritBarShown"]`
    (bool), surviving relogins (`CharacterSettings.Save()` on flip).
  - Toggle off forces hidden regardless of latch.
- Show/hide applies to **all three** SP nodes (`SpOutline`, `SpBar`, `SpText`).

## Toggle (`Scripts/UI/OptionsWindow.cs` + `Scenes/UI/OptionsWindow.tscn`)

- `Constants.Options`: add `ShowSpiritBar` and `SpiritBarShown` key constants.
- Options window: add `ShowSpiritBarCheck` CheckBox under `TargetFilteringCheck`
  (offsets 56–80; grow window `offset_bottom` 100 → 108).
- Wire exactly like `TargetFilteringCheck`: initialize from
  `CharacterSettings.GetOption<bool>(Options.ShowSpiritBar, true)`, write back on
  `Toggled`, `Save()` on window close.

## Out of scope

- Overhead (in-world) spirit bars — server VPU packet doesn't carry SP; user confirmed
  SP is never shown to other players.
- Party window spirit bars.
- Protocol changes.
- Modifying `vitals-outline.png` (the SP panel is a separate texture).

## Testing

- Unit test the visibility decision (option × latch × MaxSP>0) as a small pure function
  (`SpiritBarVisibility.ShouldShow`) in `tests/Goose2Client.Tests`.
  (Settings-bool round-trip is already covered by
  `CharacterSettingsJsonTests.RoundTrip_SerializesAndDeserializesAllFields`.)
- Existing suite must stay green (262 passing at baseline).
- Manual: toggle on/off, SNF with SP 0 then >0, relogin latch persistence, hidden-state
  window byte-identical to pre-change.

## Task estimate

~4 tasks (assets; scene; VitalsWindow behaviour; options toggle) → single plan.
