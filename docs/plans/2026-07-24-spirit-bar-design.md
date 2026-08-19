# Spirit Bar in Vitals Window — Design

Date: 2026-07-24
Status: approved

## Summary

Add an optional **Spirit (SP) bar** to the Vitals window (`VitalsWindow.tscn`), below the
existing HP/MP bars. The bar is toggleable from the Options window, defaults to hidden
when the character has no SP (MaxSP == 0), and latches permanently visible (persisted
per character) once the server first reports MaxSP > 0.

SP is **local-player only** — no overhead bars, no party UI, no protocol changes.
`StatusInfoPacket` (SNF) already carries `MaxSP`/`CurrentSP`.

## Constraints (from user)

- The existing HP/MP layout must stay pixel-identical; when the SP bar is hidden, the
  window must look exactly like today.
- The SP bar is a plain **rectangle** (flat left and right edges).
- SP bar left edge starts at the same x as the MP bar's left edge (window x49).
- SP bar right edge ends where the MP bar's 45° bevel meets its bottom edge (window x141).
- Spacing between MP and SP = spacing between HP and MP: no gap except the 1px black
  outline line between panels.
- Window may expand with blank space to fit the bar.
- Bar height: 2–4px less tall than the MP bar (17px) → **14px**.
- Colour: green/yellow, not pure yellow (too bright).

## Assets

### `Assets/UI/vitals-sp-bar.png` (new, 93×14, RGBA)

Plain rectangle, window coords x49–141 (93px wide), 14px tall. Same three-tone vertical
striping as the MP bar texture (`vitals-mp-bar.png`):

| Row | Colour | RGB |
|-----|--------|-----|
| y0 (top) | highlight | (190, 190, 90) |
| y1–y12 | body | (125, 125, 35) |
| y13 (bottom) | dark | (82, 82, 23) |

No bevels, no transparent margins — the shape fills the texture edge to edge.
(Colors are a starting point; tunable without layout impact.)

### `Assets/UI/vitals-outline.png` (modified: 183×55 → 183×61)

Precise paint spec (existing MP panel reference geometry: black line rows, 1px black
side borders, interior (59,59,59) first row / (96,96,96) middle / (166,166,166) last row):

1. Start from the current 183×55 image. Rows y0–y44 untouched.
2. Extend the existing black line at y45 (currently x47–140) rightward to x47–142 —
   it becomes the shared MP-bottom / SP-top line (same as y27 for HP/MP).
3. Paint the rectangular SP panel, rows y46–y59 (rows y46–y54 already exist with only
   the portrait-circle arc on the left — paint only x47–142; rows y55–y59 are new):
   - rows y46–y59: black border at x47 and x142
   - y46: fill x48–141 = (59,59,59)
   - y47–y58: fill x48–141 = (96,96,96)
   - y59: fill x48–141 = (166,166,166)
4. Append row y60: black x47–142, rest transparent. New size 183×61.

The SP bar (scene rows y46–59) sits on top of this track; the MP bar's dark bottom row
(y44) + shared black line (y45) reproduce the exact HP/MP inter-bar spacing. The
portrait circle (arc ends at y54) and level circle keep their positions; ~6px of
transparent space remains below the circle.

### Scene geometry (`Scenes/UI/VitalsWindow.tscn`)

| Node | Change |
|------|--------|
| `VitalsWindow` (root) | `offset_bottom` 63 → 69 (window 55 → 61 tall) |
| `Background` (TextureRect) | `offset_bottom` 55 → 61 |
| `SpBar` (new TextureProgressBar) | x49–142, y46–60 (93×14), `texture_progress` = vitals-sp-bar.png, `fill_mode` = 0, `mouse_filter` = 0, `max_value` = 1 |
| `SpText` (new Label) | mirrors `MpText` (mouse_filter=1, left-aligned, bottom-v-aligned) at x59–152, y46–60 |

`HpBar`, `MpBar`, `HpText`, `MpText`, `Portrait`, `LevelCircle`, `LevelText`: unchanged.

## Behaviour (`Scripts/UI/VitalsWindow.cs`)

- On `SNF` (`StatusInfoPacket`): set `_spBar.Value = MaxSP == 0 ? 0 : CurrentSP/MaxSP`,
  `SpText` to `CurrentSP.ToString("N0")`, tooltip to `Spirit: {CurrentSP:N0} / {MaxSP:N0}`
  (same pattern as HP/MP, including hover wiring on bar + label).
- **Visibility** (evaluated on every `SNF` and on option change; cheap direct read):

  ```
  shown = ShowSpiritBar option (default true)
        && (SpiritBarShown latch  ||  p.MaxSP > 0)
  ```

  - When `MaxSP > 0`: set latch → save (once); bar shows.
  - When `MaxSP == 0` and never latched: bar hidden.
  - Latch persists per character in `CharacterSettings.Options["SpiritBarShown"]`
    (bool), surviving relogins. `CharacterSettings.Save()` on flip.
  - Toggle off forces hidden regardless of latch.
- Initial visibility before first `SNF`: hidden.

## Toggle (`Scripts/UI/OptionsWindow.cs` + `Scenes/UI/OptionsWindow.tscn`)

- `Constants.Options`: add `public const string ShowSpiritBar = "ShowSpiritBar";`
- Options window: add `ShowSpiritBarCheck` CheckBox under `TargetFilteringCheck`
  (offsets 56–80; grow window `offset_bottom` 100 → 108).
- Wire exactly like `TargetFilteringCheck`: initialize from
  `CharacterSettings.GetOption<bool>(Options.ShowSpiritBar, true)`, write back on
  `Toggled`, `Save()` on window close.
- VitalsWindow reads the option live (no signal needed — same read-on-demand pattern as
  `SpellTargetManager` for Target Filtering).

## Out of scope

- Overhead (in-world) spirit bars — server VPU packet doesn't carry SP; user confirmed
  SP is never shown to other players.
- Party window spirit bars.
- Protocol changes.

## Testing

- Unit test the visibility decision (option × latch × MaxSP>0) as a small pure function
  (e.g. `SpiritBarVisibility.ShouldShow(optionOn, latched, maxSp)`) in
  `tests/Goose2Client.Tests`.
- Existing suite must stay green (262 passing at baseline).
- Manual: toggle on/off, SNF with SP 0 then >0, relogin latch persistence.

## Task estimate

~4 tasks (assets; scene; VitalsWindow behaviour; options toggle) → single plan.
