# UI Scale Part 1C — Dynamic Surfaces & Runtime Audit

**Part order:** 1A → 1B → 1C → Part 2 (sequential, same worktree/branch; each part is a self-contained execution context with its own task list and commit sequence). **Prereq: Part 1B is merged** (this part registers dynamic surfaces with the applier and runs the headless audit that gates the whole part chain).

**Goal:** Everything that is NOT a static tscn-offset surface: factor-aware per-frame tooltip layout, the vitals portrait, the font-override migration (incl. the Party-tile and BuildStamp exceptions), the runtime-created Quest/Info multi-windows, and the headless self-test that audits the whole root-viewport surface at 1× and 2×.

**Architecture (shared by all parts):** Windows keep their build-time geometry as the 1× base — `.tscn` pixel offsets load at 1× regardless of the active factor, and C# build code writes 1× base constants (it does **not** scale at build time; scaling happens in `Relayout`). A static `UiScaleLayout` helper (Part 1B) snapshots each window's descendant geometry at end-of-`_Ready` as-is (anchor-relative offsets) — that snapshot is the base, no division. `ScaleRegister()` (end of each window's `_Ready`) snapshots, registers, relays out, and places — so a window spawned at runtime under a 2× factor scales AND places in the same frame (zero 1× frames). `UiScaleApplier.Apply(factor)` (plain class, `GameManager`-hosted, `TooltipManager.Instance`-style `Instance` accessor; Part 1B) then: normalizes the factor, cancels in-flight window drags (Part 2), hides live tooltips, mutates `GameTheme.default_font_size`, re-applies registered explicit font overrides, calls each registered window's geometry-only `Relayout()`, then every `BaseWindow`'s `RepositionFromSaved()`. Placement is the **saved-quad model** (Part 1A Task 2): each window persists (position, size, factor, canvas) at drag-end, and every placement — registration, scale commit, canvas resize — derives from that quad + the live (Size, factor, canvas) via pure `WindowPlacement.ResolveScaled`; the quad is invariant across commits, so scale commits round-trip exactly and edge margins (logical px) scale with the factor.

**Requirements (stable IDs SC-01…SC-16):** see the `Requirements` table in `2026-08-21-ui-scale-design.md` — it is the canonical requirement→component→phase→test mapping; task headers in this file tag the IDs they implement.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp, `net10.0` test target), xUnit, headless `godot` self-test.

**Execution:** dedicated worktree off main (via @using-git-worktrees) — SAME worktree/branch as Parts 1A/1B (sequential dependency); the five tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). **Task 5 needs the headless `godot` binary** (`/usr/local/bin/godot`, verified) — no display, no server. Part 2's matrix M1–M9 and M11 need a display and a game server — run manually or in a headed session (M10 is this part's Task 5 headless gate, no server).

---

## APIs verified (citations)

- `BaseWindow.RepositionForCurrentCanvas()` — `Scripts/UI/BaseWindow.cs:88` — public, idempotent, "safe to call any time after `_Ready`"; first-run-dialog centers, else `WindowPlacement.Resolve(storedOrDefaultPos, Size, savedCanvas, currentCanvas)`. Reads `Size` at call time → **must run after geometry is applied** (that ordering is why the apply pass is fonts → geometry → reposition).
- `GameManager.OnWindowResized` — `Scripts/GameManager.cs:337-345` — existing live-resize precedent: walks `UiLayer`, calls `RepositionForCurrentCanvas()` on every `BaseWindow` via `CollectBaseWindows` (`Scripts/GameManager.cs:347-356`).
- `EnsureHud()` (`Scripts/GameManager.cs:323`) has exactly **one call site**: `MapManager._Ready` (`Scripts/MapManager.cs:93`), a server-driven map transition — at startup (login screen) **no HUD exists**. The applier is created in `GameManager._Ready`, which precedes any window's `_Ready` (windows first build when `EnsureHud` runs). A headless self-test that needs the HUD must call `EnsureHud()` itself (Part 1C Task 5).
- `CharacterSettings.Options` — `Dictionary<string, object>` with `GetOption<T>(key, default)` / indexer; `IncludeFields` JSON — `Scripts/CharacterSettings.cs:42-67`. Key constants live in `Constants.cs:136` (`public static class Options`). Part 2 adds the two new OPTION keys (`UiScaleMode`/`UiScaleValue`); the parts of this feature add per-window QUAD fields `Size`/`Factor` (Part 1A Part 1A Task 2 — written at drag-end), NOT option keys.
- `WindowPlacement.Resolve(savedPos, windowSize, savedCanvas, currentCanvas)` — `Scripts/UI/WindowPlacement.cs:30`; `TitleBarHeight = 24` (`Scripts/UI/WindowPlacement.cs:17`) is the y-containment allowance.
- GodotSharp 4.7.1, verified by **reflection on the actual project DLL** (`/opt/Godot_v4.7.1-stable_mono_linux_x86_64/GodotSharp/Api/Release/GodotSharp.dll`), not docs:
  - `Theme.SetDefaultFontSize(int)` / `GetDefaultFontSize()` — the theme-mutation API.
  - `Control.AddThemeFontSizeOverride(StringName, int)` and `Control.AddThemeConstantOverride(StringName, int)` — set-or-replace; **there is no `SetThemeFontSize`/`SetThemeConstant`** in this binding (the existing codebase already uses `AddThemeFontSizeOverride`).
  - `Control.GetThemeFontSize(StringName, StringName)` / `GetThemeConstant(...)` — effective values (the constants snapshot reads `GetThemeConstant`; the Part 1C Task 5 audit reads `GetThemeFontSize`).
  - `Control.OffsetLeft/OffsetTop/OffsetRight/OffsetBottom` and `AnchorLeft/Top/Right/Bottom` — all present (reflection). The snapshot records the **offsets** (anchor-relative by construction) and never touches anchors or `Position`/`Size` directly.
  - `Control.HasThemeFontSizeOverride(StringName)` / `HasThemeConstantOverride(StringName)` — override-presence queries; the font audit (Part 1C Task 5) uses these to find ANY font-override control under `UiLayer` and demand registry membership.
  - `Control.SetMeta(StringName, Variant)` / `GetMeta(...)` — the skip-meta mechanism.
  - `Node.TreeExited` event — deregistration hook.
  - `OS.GetCmdlineUserArgs()` (`string[]`) — project args after `--`; **`GD.GetCommandLineArgs` does not exist**. Part 1C Task 5's flag comes from here.
  - Anchored roots in the wild: `Scenes/UI/ChatWindow.tscn:11-14` (bottom-left, `offset_top = -213`), `Scenes/UI/Toolbar.tscn` root bottom-right (all four anchors `1.0`). Offsets scale correctly under either; `Position` would not.
- Test project (`tests/Goose2Client.Tests`) pins **GodotSharp 4.6.2** from NuGet (not the 4.7.1 in the engine dir) — the xUnit surface (`UiScale`, `WindowPlacement` param) must stay 4.6.2-compatible; `Vector2I`/`MathF` are fine (existing `WindowPlacementTests` already use GodotSharp).
- `theme_override_font_sizes` occurrences to migrate: `Scenes/UI/BankWindow.tscn:43` (9), `Scenes/UI/ChatWindow.tscn:47` (`normal_font_size` 12, RichTextLabel), `Scenes/UI/ChatWindow.tscn:55` (12), `Scenes/UI/DebugWindow.tscn:23,32` (12), `Scenes/UI/VendorWindow.tscn:43` (10). Raw C# overrides: `Scripts/UI/BaseMultipleWindow.cs:60` (12) and `:68` (10). (Bridge files `ChatBubble`/`BattleTextLine`/`BridgedNameLabel` are world-space — **do not touch**.)
- Window geometry is `.tscn` pixel-offset layout (e.g. `Scenes/UI/VitalsWindow.tscn` all `layout_mode = 0` offsets; `Scenes/UI/BaseWindow.tscn` anchor-based children). Slots: `Scenes/UI/ItemSlot.tscn` `custom_minimum_size = Vector2(32, 32)`.
- Headless runner: `/usr/local/bin/godot --headless` (4.7.1 mono). Existing probes `tools/tests/*.gd` intentionally do NOT execute C#; Part 1's C#-executing proof is a project-argument self-test (Part 1C Task 5), run as `godot --headless -- +selftest=ui_scale`.

## Design refinements vs `2026-08-21-ui-scale-design.md` (ratified in design chat)

- R1 (revised after plan review): per-window hand-written layout constants → **generic snapshot** (`UiScaleLayout`). **Build-time geometry is the base as-is — no division.** The original divide-by-current-factor idea was wrong: `.tscn` geometry loads at 1× regardless of the active factor, so at a 1080p startup (Apply 2× before HUD build) a snapshot would divide 1× geometry by 2 and `Relayout(2)` would un-scale it — the HUD would render at 1× on exactly the displays this feature exists for, and no headless test could catch it (headless factor is always 1). Registration calls `Relayout()` once (same-frame scaling, no flash). The snapshot records **anchor-relative offsets, not Position/Size**, because anchored roots (`ChatWindow` bottom-left, `Toolbar` right-edge) must not have their `Position` scaled or they detach from their edge.
- R2: live tooltips are **hidden** on apply (design said repositioned) — re-shown on next hover; avoids mouse-follow geometry mid-commit.
- R3: windows **self-register** at end of `_Ready` (GameHud does not enumerate) — `VitalsWindow` is a plain `Control`, and runtime-spawned NPC windows (via `BaseWindow._Ready`) register automatically.

---
### Task 1: factor-aware tooltip layout (`TooltipMetrics`) — SC-09

**The problem (review finding — "hidden on apply" was a scope escape, not a solution):** the four tooltip controls compute their layout every frame from 1× constants (item: 40px text column right of the 32px icon, 9px right pad, header block to y≈46, stats from y=48, +4 bottom; spell/text: label min + (8,4); map-item: 6/4/2/4 margins + 400px label widths; item icons sit at tscn offsets 4–36). Fonts scale via the project theme but this layout would not — a 2× tooltip = 2× fonts in a 1× box.

**Files:**
- Create: `Scripts/TooltipMetrics.cs` (pure, Godot-free)
- Create: `tests/Goose2Client.Tests/TooltipMetricsTests.cs`
- Modify: `Scripts/UI/ItemTooltipControl.cs` (`_Process` per-frame layout — the 1× constants above), `Scripts/UI/SpellTooltipControl.cs`, `Scripts/UI/TextTooltipControl.cs`, `Scripts/UI/MapItemTooltipControl.cs`

**Spec:**
- `TooltipMetrics` (pure): `ItemMetrics(float factor)` → `(TextColumn, RightPad, HeaderTop, StatsTop, ExtraBottom, IconSize, IconOffset)`, `TextPad(float factor)` → `(w, h)`, `MapItemMetrics(float factor)` → `(LeftMargin, TopMargin, RowGap, BottomMargin, LabelWidth)` — each value `UiScale.ScaleSize`-scaled from the cited 1× base (the 1× row of the table MUST equal today's literals), half-away-from-zero rounding. A shared static instance (or `ScaleSize` via `UiScaleApplier.Instance.Scale`) is the only scaling entry — controls never hand-multiply.
- Each control's `_Process` reads the metrics EVERY frame from `UiScaleApplier.Instance` (the factor can change between shows; the per-frame read IS the live mechanism — no snapshot involved). The item tooltip's icon `TextureRect` gets offset/size set per-show from `ItemMetrics.IconOffset/IconSize` (replacing the tscn 4–36 offsets). Viewport clamping (`PositionTooltip`) is untouched — it uses live `Size`, which is now scaled, so clamps stay correct.
- Live tooltips still HIDE on commit (Part 1B Task 1 step 3, R2) so no per-frame reflow is needed mid-commit; on next hover they rebuild at the live factor.

**Step 1 (xUnit, red):** `TooltipMetricsTests` — full constant table at 1× (every field == today's literal), 1.5×, 2×, and 1×→2×→1× round-trip per control. **Step 2:** implement the metrics + rewire the four `_Process` bodies. **Step 3 (green):** suite + headless leg in Task 5: show the SPELL tooltip (simplest — one label) over a visible parent via its public show API, two `ProcessFrame`s, assert `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× and the y-clamp `y + size.Y <= canvas.Y`; if the spell tooltip cannot be shown headless (no suitable parent), the leg degrades to: pure-table xUnit + the Part 2 manual matrix M7 (item tooltip at 3×) — state which one landed in the commit message.
**Commit:** `feat: factor-aware per-frame tooltip layout (TooltipMetrics)`.

---

### Task 2: vitals portrait scaling (`VitalsPortraitMetrics`) — SC-09

**The problem (review finding):** `VitalsCharacterDisplay.Refresh()` → `SetLayer` writes each portrait `TextureRect`'s `Size`/`Position` from fixed 1× constants (`PortraitSize = 53f`, `HeadDropPixels = 20f`, `PortraitZoom = 1.25f`, `VitalsCharacterDisplay.cs:47-80`) AFTER the window snapshot — at 2× the frame grows but the next character update repaints the portrait at 1×. (Correction to the earlier draft: the parent `VitalsWindow` does NOT hold the layer graphic IDs — `Refresh()` queries `CurrentMapManager.LocalPlayer`'s appearance on every call, `VitalsCharacterDisplay.cs:22-36`; the re-layout path must re-query the same way, not replay cached IDs.)

**Files:**
- Create: `Scripts/VitalsPortraitMetrics.cs` (pure, Godot-free)
- Create: `tests/Goose2Client.Tests/VitalsPortraitMetricsTests.cs`
- Modify: `Scripts/UI/VitalsCharacterDisplay.cs` (`SetLayer` + new `Relayout`)
- Modify: `Scripts/UI/VitalsWindow.cs` (`Relayout` re-runs the portrait pass)

**Spec:**
- (a) Extract the pure math: `VitalsPortraitMetrics.Layout(Vector2 texSize, float factor)` → `(Vector2 rectSize, Vector2 rectPosition)` (draw size `texSize × 1.25 × f`, centered on `53 × f`, drop `20 × f`) — xUnit-pinned at 1×/1.5×/2× + round-trip; the 1× row equals today's literals.
- (b) `SetLayer` reads `UiScaleApplier.Instance.Factor` and uses the metrics.
- (c) `VitalsCharacterDisplay.Relayout()` re-queries the local player's appearance EXACTLY like `Refresh()` does (same `CurrentMapManager?.LocalPlayer` guard; if absent, no-op) and re-runs the per-layer layout for the currently displayed layers — no cached IDs anywhere. `VitalsWindow.Relayout()` calls it after the generic geometry apply.

**Step 1 (xUnit, red):** `VitalsPortraitMetricsTests`. **Step 2:** implement. **Step 3 (green):** suite. The headless self-test (Task 5) cannot assert a live portrait (no character graphics without a server) — the Part 2 manual matrix M1 covers it: portrait fills the scaled circle after a character load at 2×.
**Commit:** `feat: scale the vitals portrait with the UI factor (VitalsPortraitMetrics)`.

---

### Task 3: font-override migration (tscn + raw C# → `ApplyFontSize`) — SC-04, SC-10, SC-12

**Files:**
- Modify: `Scenes/UI/BankWindow.tscn:43`, `Scenes/UI/ChatWindow.tscn:47,55`, `Scenes/UI/DebugWindow.tscn:23,32`, `Scenes/UI/VendorWindow.tscn:43` — **remove** the `theme_override_font_sizes` lines (values migrate to C# as cited base constants).
- Modify: `Scripts/UI/BankWindow.cs`, `Scripts/UI/ChatWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/VendorWindow.cs` `_Ready`: `applier.ApplyFontSize(<the control>, <base>)` — Chat's RichTextLabel uses `prop: "normal_font_size"`.
- Create: `Scripts/PartyMemberMetrics.cs` (pure, Godot-free) + `tests/Goose2Client.Tests/PartyMemberMetricsTests.cs` — the party-tile leg of this task.
- Modify: `Scripts/UI/PartyWindow.cs` — tile `SetCustomMinimumSize` + the internal-map part of `Relayout` (the tscn-only 87×33 has no other owner; SC-10).
- Modify: `Scripts/UI/BuildStampOverlay.cs` — the fixed 10px label override (the deliberately-unscaled exception; SC-12).

**Rules:**
- The base constant in C# must equal the removed tscn value (9/12/12/12/10). At factor 1 the rendered size is unchanged — verified by Task 5's 1× audit.
- Bridge files (`ChatBubble.cs:95`, `BattleTextLine.cs:33`, `BridgedNameLabel.cs:17`) stay on raw overrides — world-space, out of scope.
- **Party roster tiles (the ONE exception to 'skip container-managed offsets'):** `PartyWindow.MemberList` (VBoxContainer) holds 8 `PartyMember` tiles (`PartyWindow.cs:19-24`); each tile's 87×33 exists ONLY as tscn offsets — no `CustomMinimumSize` — so the snapshot skips them and the VBox allocates unscaled 87×33 while the theme-scaled `NameText` font (project default — no per-control entry needed) overflows its 11px line. Fix: new pure `PartyMemberMetrics` (pattern: `TooltipMetrics`) — `For(factor)` → `(MinSize, NameRect, HpRect, MpRect, BackgroundRect)` from the 1× tscn literals (root 87×33; Name (1,0,86,11); Hp (1,11,86,21); Mp (1,22,69,32); Background (0,10,87,33)); xUnit: the 1× row equals the tscn literals. `PartyWindow.Relayout` (Part 1B Task 4 pattern) applies per tile from the existing `_members` array: `tile.SetCustomMinimumSize(m.MinSize)` + the children's offsets from the metrics — the container owns the tile's SLOT (min-size-driven), the applier owns the tile's INTERNAL map.
- **BuildStamp exception (fixed override, deliberately unregistered):** `BuildStampOverlay._Ready`'s label (`Scripts/UI/BuildStampOverlay.cs:18`) gets `label.AddThemeFontSizeOverride("font_size", 10)` — a FIXED literal, NOT routed through `ApplyFontSize`. The applier mutates the project-wide theme's DEFAULT font size, so any control without a local font override scales with it — the stamp (design: dev-only, intentionally unscaled) needs the local override to stay 10px. Task 5's self-test asserts it is still 10 at 2×.
- After this task, `grep -rn "AddThemeFontSizeOverride\|theme_override_font_sizes" Scenes Scripts | grep -v Overlays` returns only `UiScaleApplier.cs` (the helper) and `BuildStampOverlay.cs` (the documented fixed-override exception above). (The three bridge files live in `Scripts/Overlays/` — a different path from the window code — so exclude that directory rather than filename-filtering.) State this grep in the commit message body as the invariant.

**Gate:** grep invariant above; xUnit green.
**Commit:** `refactor: route all root-viewport font overrides through UiScaleApplier.ApplyFontSize`.

---

### Task 4: runtime-created multi-windows (Quest/Info) — factor-aware layout (SC-11)

**The problem (review catch — font migration alone is insufficient):** `QuestWindow`/`InfoWindow` extend `BaseMultipleWindow` — which ITSELF extends `BaseWindow` (verified, `BaseMultipleWindow.cs:14`) — and are spawned at RUNTIME by `QuestWindowManager`/`InfoWindowCreator` (`GameHud.cs` adds both managers in its step 5 — the windows themselves do not exist at HUD build time). `BaseMultipleWindow._Ready` creates `LineCount` line labels at `font_size` 10 stacked at the fixed 1× pitch `LineRowHeight = 11.18f`, and the buttons at 12 (`BaseMultipleWindow.cs:17-25,60-69`). Migrated fonts only (Task 3) would give 2× fonts on 1× pitch — overlapping lines — and a window spawned while at 2× would come up unscaled.

**Files:**
- Create: `Scripts/MultiWindowMetrics.cs` (pure, Godot-free)
- Create: `tests/Goose2Client.Tests/MultiWindowMetricsTests.cs`
- Modify: `Scripts/UI/BaseMultipleWindow.cs` (label creation + new `Relayout` + registration)

**Spec:**
- `MultiWindowMetrics` (pure; pattern: `TooltipMetrics`) — **absolute line positions, not a pitch (review: scaling a per-step pitch and multiplying by the index accumulates rounding drift, and the line ORIGIN (6,22) would stay unscaled, stranding the first line at 1× inside a 2× frame)**: `LinePosition(int index, float factor)` → `Vector2` — `index == 0`-independent, computed from the UNSCALED base `new Vector2(6f, 22f + index * 11.18f)` (`LinesOrigin` + `i × LineRowHeight`, `BaseMultipleWindow.cs:26,67`): at `factor == 1f` return the base floats EXACTLY (identity guarantee — no rounding at 1×); otherwise per-axis `Math.Max(1, (int)MathF.Round(base × factor, MidpointRounding.AwayFromZero))` (the explicit `(int)` cast — `MathF.Round` returns float). `LineFont(factor)` = `UiScale.ScaleSize(10)`, `ButtonFont(factor)` = `ScaleSize(12)`. xUnit: the 1× column equals today's floats EXACTLY for `index` 0…19 (`(6, 22)`, `(6, 33.18f)`, …); `LinePosition(0, 2f) == (12, 44)` (NOT (6,22)); `LinePosition(1, 2f) == (12, 66)`; 1.5× spot checks; 1×→2×→1× round-trip per index.
- `_Ready`: create the labels using `MultiWindowMetrics.LinePosition(i, applier.Factor)` at the LIVE factor (native scaling for windows spawned late — the applier's public read is `Factor`, Part 1B Task 1); give EACH line label the Part 1B Task 2 skip-meta (its geometry is owned by the metrics + `Relayout`, not the generic snapshot — the snapshot would rescale the already-scaled offsets and double-scale at 2×); register the button fonts through `applier.ApplyFontSize`; `ScaleRegister()` at end of `_Ready` (the tscn `GenericInfo` frame offsets scale generically; line labels are skipped).
- `public override void Relayout()`: `base.Relayout()` FIRST (the frame + generic geometry — `Relayout` is virtual on `BaseWindow` exactly for this override, Part 1B Task 3), then `label.Position = MultiWindowMetrics.LinePosition(i, applier.Factor)` per line and nothing else (fonts are the apply pass's registry step). Runs on HIDDEN windows too (cheap — `LineCount` labels), so the first show after a commit is already correct.
- The window frame size scales through the generic snapshot; the line area stays proportionally correct because frame and pitch scale by the same factor.

**Steps:** 1 (xUnit red) → 2 implement → 3 green (full suite). The in-engine leg is Task 5 step 2c.
**Commit:** `feat: factor-aware runtime multi-window layout (MultiWindowMetrics)`.

---

### Task 5: headless self-test (audit + 1× no-op + 2× smoke) — SC-15, SC-16

**Files:**
- Modify: `Scripts/GameManager.cs` (`_Ready`): read `OS.GetCmdlineUserArgs()`; if it contains `"+selftest=ui_scale"`, run the sequence below on the NEXT frame (one `ProcessFrame` await) and `GetTree().Quit(failed ? 1 : 0)`. **The sequence's first steps (review finding F1 — both required, in this order):** (1) `GameManager.Instance.LoadSettings("ui-scale-selftest")` — the HUD does not exist at startup (`EnsureHud`'s only call site is the server-driven map transition, `Scripts/MapManager.cs:93`, which headless never reaches), so the test builds it; BUT `CharacterSettings` is only created by `LoadSettings` (normally via login, `LoginScene.cs:103`), and **every** `BaseWindow._Ready` dereferences it unguarded (`Scripts/UI/BaseWindow.cs:51` — `GameManager.Instance.CharacterSettings.GetWindowSettings(...)`), with further unguarded derefs in `OptionsWindow.cs:24/28/32`, `HotbarWindow.cs:75`, `InventoryWindow.cs:51`, `CharacterWindow.cs:91`, `SpellbookWindow.cs:80` — calling `EnsureHud` first NREs on the first window. `LoadSettings` here creates in-memory defaults; nothing is written to disk (no `Save()` fires — no window close/toggle/drag happens headless). (2) **Force the 1× baseline (review finding — determinism):** `UiScaleApplier.Instance.Apply(1f, ApplyReason.Startup);` — the settings file `user://ui-scale-selftest-settings.json` (`CharacterSettings.cs:69`) MAY exist from an earlier run or be hand-written; once Part 2's `LoadSettings` hook lands it would `Apply` a persisted Manual 2×/3× and invalidate every 1× assertion below. The explicit `Apply(1f, …)` after `LoadSettings` (registry still empty — login is unregistered in Part 1) makes the baseline independent of both the headless canvas size AND any persisted selftest profile. (3) `GameManager.Instance.EnsureHud()` (plus another `ProcessFrame` await).
- Create: `tools/tests/run_ui_scale.sh` — wrapper (review: the binary name varies across machines — this workspace's C#-capable build is `/usr/local/bin/godot`, a 4.7.1 **mono** build, and some machines name it `godot-mono`): `GODOT_BIN="${GODOT_BIN:-$(command -v godot-mono || command -v godot)}"; exec "$GODOT_BIN" --headless --path "$(dirname "$0")/../.." -- +selftest=ui_scale` — `--path` because the script sits in `tools/tests/`; `GODOT_BIN` overrides for odd setups; args after `--` are what `OS.GetCmdlineUserArgs()` returns (the existing probes use `--script`; this one needs the project + C#).

**Sequence (all inside C#, `Print`-labeled steps):**
0. **Pin the headless canvas (review finding F3):** `Print` the root visible-rect size first. Headless does NOT apply the project's 1280×720 window size (probes report ~64–100 px). Every assertion below must therefore use the ACTUAL size read at runtime — never assume 1280×720. (The factor-1 baseline still holds: `AutoFactor(anything < 720) == 1`.)
1. **1× no-op:** for EVERY registered root — the whole `UiScaleApplier.Instance.RegisteredWindows` list, i.e. `BaseWindow`s AND the non-`BaseWindow` roots (Vitals, Chat, Party, Toolbar, Debug, BuffEffects, TooltipManager — review: enumerating only `BaseWindow`s leaves their round-trip identity unproven) — `await ProcessFrame` first (flush queued container layouts), snapshot each root's descendants' `(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom)` into a dict; record `Position` (the step-3 round-trip pin) for `BaseWindow`s ONLY (anchored roots stay edge-stuck by offset scaling, not by `Position` — recording them would assert on a value the model does not own); call `w.Relayout()`, **`await ProcessFrame` again**, re-read; assert bit-identical (factor forced to 1 above). The two `ProcessFrame`s matter (review finding): without them a container-managed child's offsets are compared mid-layout-pass and the assertion is flaky. Catches snapshot bugs and any `_Ready` code that sets geometry after the `ScaleRegister()` line.
2. **2× apply:** `UiScaleApplier.Instance.Apply(2f, ApplyReason.UserCommit)`. Then assert:
   - `GameTheme` (the applier's cached instance) `GetDefaultFontSize() == 20` (`Theme` has no `GetThemeFontSize` — `SetDefaultFontSize`/`GetDefaultFontSize` are the API, reflection-verified).
   - **Font audit (adversarial):** walk every `Control` under `UiLayer`; for any with `HasThemeFontSizeOverride("font_size")` or `HasThemeFontSizeOverride("normal_font_size")` (excluding nothing — bridge text lives in the world viewport, not `UiLayer`): the control MUST be in the applier's font registry, and its effective `GetThemeFontSize(prop)` must equal `base × 2`. A raw `AddThemeFontSizeOverride` added outside the registry (e.g. a future PR skipping `ApplyFontSize`) fails here. **Scope note:** the audit walks `UiLayer` only — the login scene (not under `UiLayer`) is outside it; Task 3's source grep plus Part 2 Task 5's login self-test leg cover that surface.
   - Sampled geometry: `Vitals` root `Size == (366, 110)` (tscn 183×55) and `Position == (16, 16)` (tscn 8,8); one `ItemSlot` under Inventory has `CustomMinimumSize == (64, 64)`; `ChatWindow` root offsets `OffsetTop == −426` and `OffsetBottom == −10` (tscn −213/−5 doubled — edge-stick preserved by offset scaling, NO reposition involved; ChatWindow is not a `BaseWindow`); a `BaseWindow`-derived dialog still satisfies `WindowPlacement`'s ACTUAL postcondition (mirror `WindowPlacement.cs` exactly, review finding — the old `canvas.Y - w.Size.Y` bound was wrong: production clamps y with the TITLE-BAR allowance, not the full window height): `0 <= X <= Max(0, canvas.X - w.Size.X)` and `0 <= Y <= Max(0, canvas.Y - applier.ScaleSize(24))` (at 2× that's 48, not 24, and not the window height). The margin-preservation model itself is xUnit-pinned (Part 1A Task 2) — the tiny headless canvas cannot express margins.
   - **Party allocation + tile leg (SC-10):** `PartyWindow/MemberList.GetChildCount() == 8` (tiles are created at window build, `PartyWindow.cs:19-24` — no server data required), AND one `PartyMember` has `CustomMinimumSize == (174, 66)` and its `NameText` `OffsetBottom == 22` (tscn 11 doubled).
   - **BuildStamp exception (Task 3):** the `BuildIdLabel` under the GameManager autoload's `BuildStampOverlay` CanvasLayer has `GetThemeFontSize("font_size") == 10` at 2× (the fixed local override beats the theme default's 20 — the design's unscaled-stamp guarantee).
   - All four tooltips hidden (R2).
   - **Tooltip live-size leg (Task 1):** the spell-tooltip show leg — `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× + y-clamp — or its stated fallback (pure-table + M7), whichever landed.
   - **Font registry lookup** uses `applier.TryGetFontBase(c, prop, out base)` + `applier.Theme` (the seams from Part 1B Task 1) — no reflection, no friend access.
2b. **Runtime-spawn leg (adversarial, THE regression the review caught):** with the HUD at 2× (nothing in step 2 has changed the factor — the legs that apply 1× come AFTER this one), instantiate a fresh window (`GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate()` — pick any window whose `_Ready` needs no server state; verify at implementation time), `UiLayer.AddChild`, await a frame; assert a sampled rect equals `round(base×2)` (e.g. its `Content` child's offsets doubled). This is the leg that fails if anyone re-introduces divide-by-factor snapshotting or build-time scaling — headless factor-1 bias cannot fake it. `QueueFree()` afterwards, then **await another process frame and assert the temp window is GONE from `UiScaleApplier.Instance.RegisteredWindows`** (the `tree_exited` prune, Part 1B Task 1) BEFORE step 3 — `QueueFree` is deferred, so a freed-but-not-yet-removed registration would otherwise have a registry entry with NO step-1 baseline (review finding).
2c. **Runtime multi-window leg (SC-11, Task 4):** still at 2×, instantiate `res://Scenes/UI/InfoWindow.tscn`, `UiLayer.AddChild`, await a frame; drive it with `new MakeWindowPacket()` (public fields — set the same values the builder at `Scripts/Network/Packets/MakeWindowPacket.cs:21` sets) via its internal `OnMakeWindow`, plus one `OnWindowLine`; assert **absolute** line positions: line 0 `Position == MultiWindowMetrics.LinePosition(0, 2f)` — i.e. `(12, 44)`, the ORIGIN scaled, not just the pitch — line 19 `== LinePosition(19, 2f)`, and line 0 `GetThemeFontSize("font_size") == 20`. THEN `Apply(1f)`: line 0 back to EXACTLY `(6, 22)` and line 19 to `(6, 22 + 19×11.18f)` as floats (identity, no rounding at 1×). `QueueFree` + await + registration gone (same cleanup rule as 2b). Ordering is load-bearing (review): 2b runs BEFORE 2c's 1× apply — the old draft called `Apply(1f)` inside 2c and then expected 2b's spawn to be 2×.
2d. **Saved-origin round-trip leg (SC-07, review — a saved (0,0) must survive):** pick one registered `BaseWindow` (e.g. `Vitals`); call `GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, new Vector2(0, 0), true, canvas, thatWindow.Size, 1f)` + `Save()` (a drag to the origin, simulated — the drag itself can't be synthesized headless); reload the SAME selftest profile through `LoadSettings` (fresh `CharacterSettings`), `ProcessFrame`, then call the window's `RepositionFromSaved()`; assert `Position == (0, 0)` (the `Placed` marker honors it — without it the window would snap to its default layout). Restore the profile afterwards (re-save the original quad or delete the file) so the leg is repeatable and later runs start clean.
3. **Back to 1×:** already at 1× after 2c — the `Apply(1f, UserCommit)` call here probes the EARLY-RETURN path (factor unchanged → no mutation) and must leave everything untouched; then re-assert the step-1 geometry dict equality (idempotence in both directions — catches records that baked in a scaled base), the party tile `CustomMinimumSize` back to (87, 33); **AND** assert every registered `BaseWindow`'s `Position` is EXACTLY its step-1 recorded 1× position (review finding F1, pinned end-to-end: the saved-quad model derives both the 2× and the restored 1× position from the SAME invariant quad + canvas + factor, so the round-trip is exact by construction — a capture-only or stale-position model fails this assertion).

**Red/green:** run before Part 1B Tasks 3/4 land → expected FAIL (no registered windows / no `IScalableWindow`); after Task 3 → PASS. If it NREs in a window `_Ready` on `CharacterSettings`, the `LoadSettings` step is missing (finding F1); if it fails with "no windows registered", the `EnsureHud()` step is missing — fix the test, not the product code. Final state of this task is the gate for the whole part: `bash tools/tests/run_ui_scale.sh` exits 0 with labeled `OK` lines, no `ERR_`/script-error output.

**Loading coverage (SC-16, corrected per review):** the loading overlay is NOT manual-only — a server-driven map transition is not required: Part 2 Task 5's self-test leg instantiates `res://Scenes/LoadingMap.tscn` directly, adds it to the root, and pins its 1×→2×→1× round-trip in the same headless run. M8 keeps the REAL-transition sanity check (loading visible while a map actually loads); the self-test leg is the regression gate.

**Commit:** `test: headless ui-scale self-test (1x no-op, 2x audit, idempotence)`.

---


---

## Invariant-to-test matrix (Part 1C)

| Invariant | Proved by |
|-----------|-----------|
| 1× is bit-identical to today (no visual regression at default) — SC-05 | Task 5 steps 1 & 3 (audit covers EVERY registered root — `BaseWindow`s + Vitals/Chat/Party/Toolbar/Debug/BuffEffects/TooltipManager) |
| Bottom-anchored root stays edge-stuck after scaling — SC-05 | Task 5 step 2 (ChatWindow offsets `−213/−5` → `−426/−10`) |
| Geometry scales per window incl. min-sizes — SC-05 | Task 5 step 2 (sampled rects) |
| Fonts scale through the registry; raw overrides trip the wire — SC-04 | Task 5 step 2 (audit) + Task 3 grep invariant |
| Party roster tiles: 8 allocated, min-size + internal map scale — SC-10 | `PartyMemberMetrics` xUnit (Task 3) + Task 5 party leg (`GetChildCount() == 8`; (174,66) @2×) |
| Dev build stamp stays 10px at 2× — SC-12 | Task 3 fixed override + Task 5 stamp leg |
| Runtime-created Quest/Info windows: native-scale at spawn, re-layout on commit — SC-11 | `MultiWindowMetrics` xUnit (Task 4) + Task 5 step 2c (Info-window leg) |
| Tooltips hidden on commit (R2); per-frame factor-aware layout — SC-09 | Task 5 step 2 + Task 1 xUnit tables + Part 2 M7 (item tooltip at 3×) |
| Saved (0,0) is a position, not an absence — SC-07 | Part 1A Task 2 `WindowSettings_SavedOriginRoundTrips` (JSON) + Task 5 step 2d (in-engine round-trip) |
| Loading overlay scales — SC-16 | Part 2 Task 5 self-test leg (direct `LoadingMap.tscn` instantiation — no server transition needed) + M8 (real-transition sanity) |

**Explicitly deferred to Part 2:** options UI (slider/mode), `Options.UiScaleMode`/`UiScaleValue` persistence + startup read (Part 1B Task 1's pre-login Auto `Apply(AutoFactor(canvas.Y), Startup)` becomes settings-driven), auto-mode window-resize path (`GameManager.cs:103` `size_changed` handler), login/loading registration (their scenes attach no per-scene theme, but `project.godot:37` sets `theme/custom` project-wide — their text already resolves at `font_size == 10` through the applier's theme, so Part 2 Task 5 is GEOMETRY-ONLY registration; the 10→20→10 round-trip pins it, and its self-test leg instantiates `LoadingMap.tscn` directly so loading is headless-automated, not manual-only), drag-cancel on commit, manual verification matrix (incl. the new M11 mode-switching leg), and the design's accepted-limitations list.
