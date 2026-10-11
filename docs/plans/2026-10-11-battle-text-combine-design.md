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

A packet is mergeable when its text parses as a **signed number with an optional
whitespace-separated trailing suffix**. Group separators may be `,` or a space; the server uses
a **space** for XP (`Player.cs:1892-1893`, `NumberGroupSeparator = " "`) because `,` is the
packet field delimiter (`PacketParser.cs:13`) and cannot appear in BT text at all:

| Packet | Parse | Merge key |
|---|---|---|
| `Red1`, `-123` | −123, suffix "" | (Red, "") — damage |
| `Green7`, `+456` | +456, suffix "" | (Green, "") — heal |
| `Yellow60`, `+1 234 XP` | +1234, suffix "XP" | (Yellow, "XP") — XP |
| `Yellow60`, `Level Up!` / `Fizzle` | no parse | not mergeable |
| `Yellow60`, `5s ` / `1m 5s ` / `500ms` | no parse (suffix must be whitespace-separated) | not mergeable |
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
  in place (`-12` → `-37` → `-85`). While accumulating the line **rises at the normal rate**
  (32 px/s) but never expires. Each hit resets the gap timer.
- **Finalize** — the accumulator closes when either comes first:
  - **Gap:** ~1.0 s with no hit for that key, or
  - **Cap:** ~1.0 s total age (so endless spam produces a fresh line every ≤1 s instead of
    growing one number forever).

  On finalize the line keeps rising at the normal speed, but its total travel is capped at
  48 px (`RiseCapPixels`, versus 32 px for a plain line): the remaining budget sets its
  post-finalize lifetime (16 px = 0.5 s at the 1 s cap), then it fades like a plain line.

Display format for merged totals: `(total >= 0 ? "+" : "") + total.ToString("N0",
CultureInfo.InvariantCulture)` plus `" " + suffix` when the key has one — damage stays
negative, heals/XP positive, XP keeps separators and suffix. Merged totals render with
invariant `,` grouping even though the server sends XP with spaces: the client's rendering is
its own choice, and the parse rule accepts both forms.

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
- Records: key → (running total, age, since-last-hit). Constants: gap 1.0 s, cap 1.0 s.

### 3. `BattleTextLine` — accumulating mode

- `Initialize` gains an `accumulating` flag: an accumulating line starts
  `Lifetime = new OverlayLifetime(double.PositiveInfinity, risePixelsPerSecond: 32)` — it
  rises like a plain line but never expires (the manager's cap always closes it).
  Non-accumulating lines keep starting the 1 s/32 px lifetime as today. `Tick` runs in both
  modes, so it stays the only per-frame writer of `Position`; push-ups and scale changes flow
  through `_baseOffset`.
- `SetAccumulatedText(string)` rewrites the label text.
- `BeginRise()` folds the rise already travelled into `_baseOffset` (a fresh lifetime's
  rise restarts from zero, so without this the line snaps back down) and sets a
  `32 px/s` lifetime of `min(1.0, (RiseCapPixels − risen) / 32)` — the line ends at whichever
  comes first, the 1 s readability window or the 48 px total-travel cap.
- No label resizing: `Label.clip_text` defaults to false and labels are centered, so long
  totals overflow symmetrically and stay readable without touching the `ScreenBounds` cull rect.

### 4. `BattleText`

`AddText` returns the created `BattleTextLine?` (null when the 18-line cap refuses it) so the
caller can hold the handle. Accumulating lines occupy cap slots that would otherwise expire,
so keyword spam hits the 18-line ceiling slightly sooner than today. Jitter, push-ups, and
the cap work as today, and they run against accumulating lines too (they read
`CurrentScreenOffsetY()`), so a second simultaneous accumulator or a keyword line never
overlaps one. The one adjustment: `ScreenBounds` grows its worst-case vertical extent by the
combined line's 48 px rise cap (`RiseCapPixels`) versus a plain line's 32 px.

### 5. `Character` glue

`AddBattleText`: if `GameManager.Instance.CombineBattleText` and `BattleTextMergeRule.TryGet`
parses, consult the manager:

- **Start** → `container.AddText(...)` spawns the line and the manager records it (only if
  the returned line is non-null — at the cap the packet drops, as spam drops today).
- **Accumulate** → `line.SetAccumulatedText(display)` on the held handle.
- **PassThrough**, option off, or unparseable → `container.AddText(...)` as today.

`_Process` calls `manager.Tick(delta)` and `BeginRise()`s each finalized key's line. Held line
refs are checked with `GodotObject.IsInstanceValid` **and** against the current container
(`handle.GetParent() == _battleText`): `WorldTextBridge.Clear()` frees the container during
`ChangeMap` while the character lives for ~2 more frames, and `IsInstanceValid` still returns
true for a `QueueFree`d-but-not-yet-freed object, so identity is the only reliable staleness
check. A stale handle drops its record so the next packet starts fresh. The manager dies with
its `Character`.

### 6. Setting

- `Options.CombineBattleText = "CombineBattleText"` (`Scripts/Constants.cs`), bool, default
  `true`.
- `GameManager.CombineBattleText` cached bool, read in `LoadSettings` beside the `NameDisplay`
  line so the per-packet check is a field read.
- `Scenes/UI/OptionsWindow.tscn`: one new `CheckBox` row "Combine damage & heal numbers";
  wired in `Scripts/UI/OptionsWindow.cs` like `TargetFiltering` — seed from the setting, on
  `Toggled` write the option, `Save()`, update `GameManager.CombineBattleText`.

Toggling **off** needs no explicit finalize: the manager keeps ticking regardless, so the cap
closes any accumulating line within ≤1 s. Toggling **on** applies from the next packet.

## Edge cases

- **18-line cap:** refused spawn → no accumulator registered.
- **Container freed under us** (`WorldTextBridge.Clear()` on map change) **/ character
  despawn:** container-identity + `IsInstanceValid` drop path; manager lifetime is the
  character's.
- **Mixed traffic:** keyword lines and unparseable yellows take the normal spawn path and push
  accumulating lines up like any other line — accumulating lines tick, so `PlanPushUps` sees
  their drifted position via `CurrentScreenOffsetY()`.
- **Damage + heal simultaneously:** two independent accumulators; the second `Start` pushes
  the first (slowly rising) line up via the existing `PlanPushUps`.
- **Toggle off mid-accumulation:** finalizes within ≤1 s via the cap.

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
