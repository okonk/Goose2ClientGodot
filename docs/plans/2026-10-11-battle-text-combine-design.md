# Battle Text Combine — Design

Client-only option that merges repeated damage/heal/XP numbers into accumulating lines so
combat-text spam stops obscuring characters. No server or wire changes — everything needed
(value, type, target) already arrives in each `BT` packet.

## Problem

Every `BattleTextPacket` spawns a rising `BattleTextLine` above the character
(`BattleText.AddText`, up to 18 stacked, pushed up to avoid overlap). During a fight the
result is a tall column of numbers drawn over the sprite.

## Behaviour

A new Options-window checkbox **"Combine damage & heal numbers"** (default **on**) enables
accumulation. With it off, behaviour is exactly today's.

### Merge rule

A packet is mergeable when its text parses as a **signed number with an optional trailing
suffix** (invariant culture, thousands separators allowed):

| Packet | Parse | Merge key |
|---|---|---|
| `Red1`, `-123` | −123, suffix "" | (Red, "") — damage |
| `Green7`, `+456` | +456, suffix "" | (Green, "") — heal |
| `Yellow60`, `+1,234 XP` | +1234, suffix "XP" | (Yellow, "XP") — XP |
| `Yellow60`, `Level Up!` / `Fizzle` | no parse | not mergeable |
| `Stunned50` / `Miss21` / … | keyword, no parse | not mergeable |

The merge key is **(color family, suffix)**: all `Red*` with the same suffix share one
accumulator, likewise `Green*` and `Yellow60`. The server currently emits only `Red1`
(damage), `Green7` (heal), `Yellow60` (free-form), and the keyword types, so in practice this
is one damage + one heal accumulator per character, plus an XP one while grinding.
Non-mergeable lines render exactly as today.

### Accumulator lifecycle

- **Start** — the first mergeable hit for a key spawns a normal line at the anchor (x jitter
  and push-ups still apply) and opens an accumulator holding the running total.
- **Accumulate** — each later hit for the key adds to the total and rewrites the line's text
  in place (`-12` → `-37` → `-85`). While accumulating the line is **pinned at the anchor**:
  no rise, no expiry. Each hit resets the gap timer.
- **Finalize** — the accumulator closes when either comes first:
  - **Gap:** ~1.0 s with no hit for that key, or
  - **Cap:** ~2.0 s total age (so endless spam produces a fresh line every ≤2 s instead of
    pinning one growing number forever).

  On finalize the line starts its normal 1 s rise-and-fade and behaves like a plain line.

Display format for merged totals: `(total > 0 ? "+" : "") + total.ToString("N0",
CultureInfo.InvariantCulture)` plus `" " + suffix` when the key has one — damage stays
negative, heals/XP positive, XP keeps its separators and suffix.

Damage and heal accumulators are independent, so a character shows at most one red + one
green accumulating line, plus keyword lines as before.

## Client components

### 1. `BattleTextMergeRule` (new, pure) — `Scripts/Overlays/`

`TryGet(BattleTextType type, string text, out MergeKey key, out long value)` — parses the
text, maps the type to a color family (Red\*/Green\*/Yellow; keywords never merge), and
returns the merge key. `MergeKey` is a small comparable value type (family + suffix). Also
owns `Format(key, total)` for the display string.

### 2. `BattleTextManager` (new, pure C# state machine) — `Scripts/Overlays/`

One instance per `Character`, created lazily beside the container. No Godot references.

- `OnPacket(key, value, text)` → `MergeResult` describing one action:
  **Start(displayText)**, **Accumulate(displayText)**, or **PassThrough**.
- `Tick(delta)` advances age + since-last-hit per record and returns the keys that
  **Finalized**.
- Records: key → (running total, age, since-last-hit). Constants: gap 1.0 s, cap 2.0 s.

### 3. `BattleTextLine` — accumulating mode

- `Initialize` gains an `accumulating` flag: sets `Position` once from `baseOffset` and
  leaves `Lifetime = null` (`WorldOverlay._Process` already no-ops on null, so the line sits
  at the anchor without rising or expiring). Non-accumulating lines keep starting the
  1 s/32 px lifetime as today.
- `SetAccumulatedText(string)` rewrites the label text.
- `BeginRise()` sets `Lifetime = new OverlayLifetime(1.0, risePixelsPerSecond: 32)`.
- `ApplyScale` recomputes `Position` from `_baseOffset` when `Lifetime == null`, so a UI/world
  scale change doesn't leave a pinned line mispositioned (`Tick` won't fix it because it
  doesn't run while accumulating).
- Label auto-sizes its width to the text so long totals (`+12,345,678 XP`) don't clip.

### 4. `BattleText`

`AddText` returns the created `BattleTextLine?` (null when the 18-line cap refuses it) so the
caller can hold the handle. No other change — jitter, push-ups, and the cap work as today,
and they run against pinned lines too (they read `CurrentScreenOffsetY()`), so a second
simultaneous accumulator or a keyword line never overlaps a pinned one.

### 5. `Character` glue

`AddBattleText`: if `GameManager.Instance.CombineBattleText` and `BattleTextMergeRule.TryGet`
parses, consult the manager:

- **Start** → `container.AddText(...)` spawns the line and the manager records it (only if
  the returned line is non-null — at the cap the packet drops, as spam drops today).
- **Accumulate** → `line.SetAccumulatedText(display)` on the held handle.
- **PassThrough**, option off, or unparseable → `container.AddText(...)` as today.

`_Process` calls `manager.Tick(delta)` and `BeginRise()`s each finalized key's line. Held
line refs are checked with `GodotObject.IsInstanceValid` first (the bridge sweep can free the
container); an invalid handle drops the record so the next packet starts fresh. The manager
dies with its `Character`.

### 6. Setting

- `Options.CombineBattleText = "CombineBattleText"` (`Scripts/Constants.cs`), bool, default
  `true`.
- `GameManager.CombineBattleText` cached bool, read in `LoadCharacterSettings` beside the
  `NameDisplay` line so the per-packet check is a field read.
- `Scenes/UI/OptionsWindow.tscn`: one new `CheckBox` row "Combine damage & heal numbers";
  wired in `Scripts/UI/OptionsWindow.cs` like `TargetFiltering` — seed from the setting, on
  `Toggled` write the option, `Save()`, update `GameManager.CombineBattleText`.

Toggling **off** needs no explicit finalize: the manager keeps ticking regardless, so the cap
closes any pinned line within ≤2 s. Toggling **on** applies from the next packet.

## Edge cases

- **18-line cap:** refused spawn → no accumulator registered.
- **Container freed under us / character despawn:** `IsInstanceValid` drop path; manager
  lifetime is the character's.
- **Mixed traffic:** keyword lines and unparseable yellows take the normal spawn path and
  push pinned lines up like any other line.
- **Damage + heal simultaneously:** two independent accumulators; the second `Start` pushes
  the pinned first line up via the existing `PlanPushUps`.
- **Toggle off mid-accumulation:** finalizes within ≤2 s via the cap.

## Tests

`tests/Goose2Client.Tests`, pure-logic style matching `BattleTextLayoutTests`:

- `BattleTextMergeRule`: parse matrix — `-123`, `+456`, `+1,234 XP`, `Level Up!`, `Fizzle`,
  malformed (`+1,2,3`, `12KB`, empty), keyword types; key derivation (same family+suffix →
  same key, different suffix → different key); `Format` output incl. N0 grouping and sign.
- `BattleTextManager`: Start → Accumulate totals/formatting → gap finalize → cap finalize;
  key isolation (damage/heal/XP never cross); PassThrough for unparseable; tick math with
  synthetic deltas.
- OptionsWindow scene/contract test for the new checkbox, following the
  `KeyBindingsSceneTests` pattern.

## Notes / deferred

- **Default-on changes behaviour for existing players** (totals instead of per-hit numbers
  until unchecked) — intended, same call as the Name Display default change.
- **Gap/cap constants are not user-tunable.**
- **Color-family grouping** would silently mix totals if the server later gives `Red2`/`Red4`
  a distinct meaning; today it never sends them, and we control the server.
- **Crits** are not distinguished by the server, so no crit-aware handling.
