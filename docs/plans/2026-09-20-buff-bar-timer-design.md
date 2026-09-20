# Buff bar timer — design

Show the remaining time on each buff icon: a sweep that fills up as the buff
approaches expiry, a countdown label, a red tint in the final 10s, and an icon
blink in the final 15s.

Spans two repos:

- `illutiagooseserver` (worktree `.worktrees/buff-bar-timer`) — protocol + server
- `Goose2ClientGodot` (worktree `.worktrees/buff-bar-timer`) — packet + UI

## Protocol

Append a 5th field to the `BUF` packet: total buff duration in milliseconds.

```
BUF<slot>,<graphic>,<file>,<name>,<durationMs>
BUF3,5,12,Speed,120000
BUF3                              (empty slot, unchanged)
```

- Duration is last so buff names containing commas stay safe.
- `durationMs = 0` for permanent buffs (`SpellEffect.Duration == 0`); the
  client shows no sweep for those.
- Old clients stop parsing at the name and are unaffected. A client pointed at
  an un-updated server gets no duration field and shows no sweep.
- No new packet type, no periodic traffic. The server already re-sends the full
  bar on every add/renew/remove (and on login/map load), which is exactly when
  the client countdown must reset.

Known limitation (accepted): the client counts down from packet-arrival time
against its own clock, so latency/clock drift makes the sweep run slightly
fast or slow, and the icon may visually expire a beat before the server's
`BuffExpireEvent` removes the buff. The full-bar resend on removal corrects it
immediately. Same class of imprecision the hotbar cooldowns (`CDR`) already
accept.

## Server changes (`illutiagooseserver`)

- `P.BuffBar` (`Goose/Packets.cs`): signature becomes
  `Func<Buff?, int, long, string>` (buff, index, durationMs). Appends
  `,<durationMs>` when the buff is non-null; empty-slot form unchanged.
- `Player.SendBuffBar` (`Goose/Player.cs`, the only call site): computes
  `buff.SpellEffect.Duration * 1000 / world.TimerFrequency` (Duration is in
  ticks; `TimerFrequency` is ticks/second) and passes it to `P.BuffBar`.

## Client changes (`Goose2ClientGodot`)

### Packet

`BuffBarPacket` gains `long DurationMs` (default 0), parsed as the 5th field
when `LengthRemaining() > 0`. Missing field → 0 → no sweep.

### Sweep overlay

- Add a `CooldownOverlay` node to `Scenes/UI/BuffEffect.tscn` (same pattern as
  `HotbarSlot`).
- Extend `CooldownOverlay` with:
  - a growth-mode flag: the pie covers `1 - remaining/total` of the circle,
    starting empty and sweeping clockwise from the top until fully covered at
    expiry. The countdown label still shows *remaining* time via the existing
    `FormatCountdown`.
  - a tint: the pie draws red (instead of black) while
    `remaining <= dangerSeconds` (10s).

### Blink

`BuffEffect._Process` pulses the `Icon` modulate alpha between 1.0 and ~0.3 on
a ~1 Hz sine while `remaining <= 15s` and the buff has a duration. Modulate
resets to 1.0 on clear/renew. `_Process` must no-op when the slot is empty
(`_effectName == null`) — Godot runs `_Process` on hidden nodes too.

### Timing

`BuffEffect.SetEffect` stores `_expiresAt = DateTimeOffset.UtcNow +
DurationMs` (local-clock convention, same as `SpellCooldownManager.Sync`).
`_Process` computes remaining each frame and drives overlay + blink. When
remaining hits 0 the pie is fully covered (red) until the server's remove
packet clears the slot; the server's `BuffExpireEvent` fires on the same
deadline, so they land together.

### Tooltip

Fill the existing `durationText` placeholder in `BuffEffect.BuildTooltip` with
the total duration (static, e.g. `Speed\n2:00`) using `FormatCountdown`.

## Edge cases

- `DurationMs == 0` (permanent/item buffs): no overlay, no blink, tooltip shows
  name only.
- Buffs shorter than 15s blink for their whole life; shorter than 10s are red
  for their whole life. Accepted.
- Renew resets the countdown because the server re-sends the full bar.
- NPC buffs are unaffected — the buff bar is player-only.

## Testing

- `BuffBarPacket` parsing: with and without the 5th field (old-server
  compatibility).
- `FormatCountdown` reuse for the tooltip (already tested).
- Server: existing `P.BuffBar`-adjacent coverage, plus a test of the
  duration-ms computation if the test harness exposes it.
- Visual behavior (sweep direction, blink, red tint) verified by hand in the
  client — the overlay/blink state is not unit-testable here.

## Deployment note

New server + old client would parse the duration into the buff *name*
(`Speed,120000`). The Godot client is the only client and ships in lockstep
with the server, so this is treated as a non-issue (no version flag).
