# World Text Bridge (Stage 2) Design

**Goal:** Render in-world text (character names, chat bubbles, battle text) at native window resolution on a root-viewport `CanvasLayer`, so it is pixel-crisp at any integer scale (Unity SDF parity), replacing Stage 1's accepted soft 2×/3× upscaled in-world text (I5).

**Scope (decided):** strictly the three text types. HP/MP bars and the spell reticle stay in-world — solid/shape graphics that upscale cleanly at integer scale.

## 1. Node structure & ownership

- **`WorldTextBridge : CanvasLayer`** — created in `GameManager._Ready`, added to the tree **between** `WorldViewport` and `UiLayer`. Layer stays default (1); tree order makes the HUD (`UiLayer`, same layer, added after) draw on top, and the bridge sits above `WorldTexture` (a Control on the root viewport's canvas, layer 0). Visual stacking: world < text < HUD (same as today).
- **Elements are children of the bridge, not of the character.** Each bridged element (name label, one chat bubble per speaker, one battle-text container per character) is a `Node2D` under the bridge storing:
  - a back-reference to its owner `Character`,
  - its local anchor offset in **world units** (name: `(-wWorld/2, -(Height + NameTopOffset))`; bubble: its current `Position`; battle text: `(0, -40)`),
  - a scale-rebuild method (`ApplyScale(S)`, §2).
- **Creation:** `Character.EnsureNameLabel` / `ShowChatBubble` / `AddBattleText` add the element to the bridge (guarded no-op if bridge/map missing) instead of `AddChild`. All layout math stays in `Character` / overlay classes; only the parent changes.
- **Cleanup:** the bridge's per-frame projection pass drops any child whose owner fails `GodotObject.IsInstanceValid` → `QueueFree`. Covers character removal, map changes (all owners freed at once — no `ChangeMap` wiring needed), and the existing bubble-replacement `QueueFree` (elements are already bridge children and leave the tree themselves). ≤1 frame of orphaned elements, invisible (loading overlay covers transitions).
- One bubble per character, one battle-text container per character: replacement logic unchanged.
- **Z-ordering:** existing constants kept (`BattleText 20` < `NamesZIndex 100` < bubble `102`, `ZAsRelative = false`); they now sort within the bridge's own canvas — same relative stacking. In-world graphics (emotes, spell FX, HP bars) are always below all bridged text; the only real order change is text-vs-emote/spell flipping to text-on-top, covered by the plan's "text always above the world" acceptance (top-down).

## 2. Projection & scaling

**Forward transform** — new method on `WorldViewport`, exact inverse of `WindowToWorld`:

```csharp
public Vector2 WorldToWindow(Vector2 worldPos)
{
    var vp = Current.GetCanvasTransform() * worldPos;   // world → sub-viewport px (float; camera lerp included)
    return vp * Layout.Scale + Layout.DisplayOrigin;    // → root-window px
}
```

Text is vector-rendered, so sub-pixel screen positions are fine — strict integer scaling (I1) only constrains the texture blit.

**Per-frame pass — driven by the bridge's own `_Process` at `ProcessPriority = 100` (frame-ordering decision, probed headless).** Two rejected alternatives: `SceneTree.process_frame` is emitted *before* node `_process` callbacks (probed, `tools/tests/text_bridge_order.gd`) — a process-frame projector would read last frame's positions (names trail the sprite); and default-priority `_Process` runs in tree order, where the `GameManager` autoload (bridge's parent) processes *before* the map scene. Priority 100 runs after every world node (all default 0: `Character`, `MapManager` camera, `WorldOverlay`) within the same processing stage, before rendering. The probe pins both engine facts; if it fails on an engine build, the design must be re-derived. Element lifetimes (bubble 3s, battle-text rise) keep their existing `WorldOverlay._Process`; only the *projection* uses the priority-100 pass.

Pass body:
1. If `WorldViewport.Current == null` (no map) → hide all elements, return.
2. For each child element: drop if owner fails `IsInstanceValid`; else `element.Position = WorldToWindow(owner.GlobalPosition) + element.LocalOffset * S`.
3. Cull: element screen rect ∉ `Rect2(Layout.DisplayOrigin, Layout.DisplaySize)` → `Visible = false` (re-show on re-entry).

**Scaling rule:** all *visual constants* are world value × S — font size (12 → 12S), outline (4 → 4S), bubble padding / corner radius / `MaxWidth`, name-label size. **Node/`Control` scale is explicitly out:** Godot rasterizes glyphs at the font size; `Control.Scale = 2` stretches the 12px rasterization (soft again). Raising `font_size` to 12S makes `TextServer` re-rasterize at 12S — crisp at native resolution. Everything measured from the font follows automatically (bubble `GetStringSize` / autowrap min-size at 12S yields S× dimensions); constants not derived from the font (outline, padding, radius, `MaxWidth`) are multiplied by S explicitly. Positions are the exception: anchor offsets stay in **world units** and are scaled once at projection time.

**Scale changes** — `WorldViewport.ApplyMode` (single layout mutator) fires a `ScaleChanged(float)` event **only when `Layout.Scale` actually changes** (1920→1921 keeps scale 2; firing always would re-measure bubbles for nothing). The bridge subscribes (wired in `GameManager._Ready`) and re-lays out every element. Per-element `ApplyScale(S)`:
- **Name:** font/outline ×S, re-measure, recompute world offset from `owner.Height + NameTopOffset`.
- **Bubble:** re-run the full measurement at 12S with scaled `ChatBubbleLayout` constants, re-anchor above the nameplate using `owner.Height`.
- **Battle text:** container offset `(0,-40)` is scale-invariant world units; lines re-apply font/outline and the 100×16 label box ×S; spread offsets and the 32px/s rise stay world units.

A 1-frame visual pop during a mid-flight resize (bubble re-measure, lines re-font) is accepted over pixel-perfect preservation of per-line progress.

**Testable seam** — pure static helper (compiles in the xUnit project; GodotSharp math types only): `Project(worldPos, Transform2D canvas, float scale, Vector2I origin) → Vector2` and `IsCulled(Rect2 element, Rect2 displayRect) → bool`.

## 3. Per-element changes

**`Character.cs`** — parent + scale parameters change; layout math stays put:
- **Name label:** created with `fontSize = 12S, outline = 4S`; added to the bridge; text updates (`SetAppearance`) unchanged. `RepositionOverlays` changes from setting `_nameLabel.Position` to pushing the recomputed world offset into the bridge element. (Only `Character.cs` touches `_nameLabel` — verified by grep in the plan.)
- **Chat bubble:** `SetText(message)` measures at 12S with scaled `ChatBubbleLayout` constants; `BackgroundWidth` reported in **world units** (screenWidth / S) so `ShowChatBubble`'s centering/anchoring is untouched. Replacement `QueueFree` unchanged.
- **Battle text:** container added to the bridge at world offset `(0,-40)`; `BattleTextLine.Initialize` scales font/outline/label box. Rise/spread unchanged.
- **Untouched:** HP/MP bars, emotes, spell animations, reticle, `Height`.

**Input parity (Stage 1 I6 must survive):** the bubble's `Panel` background defaults to `MouseFilter.Stop` — inert in the sub-viewport today, but would **swallow real root clicks** once in the root viewport. Set `MouseFilter.Ignore` on it (label already `Ignore`); all other bridged Controls get `Ignore` explicitly. Result: clicking a bubble still produces a world click, exactly as Stage 1 signed off.

**Files:** `Scripts/WorldTextBridge.cs` (new), `Scripts/WorldTextProjection.cs` (new, pure), `Scripts/WorldViewport.cs` (+`WorldToWindow`, +`ScaleChanged`), `Scripts/GameManager.cs` (bridge creation/wiring), `Scripts/Character/Character.cs`, `Scripts/Overlays/ChatBubble.cs`, `Scripts/Overlays/BattleText.cs`, `Scripts/Overlays/BattleTextLine.cs`, `Scripts/Overlays/ChatBubbleLayout.cs`, + tests.

## 4. Edge cases & error handling

- **No map attached:** bridge hides all elements, skips projection (`Current == null`). Element creation is impossible pre-map anyway (all created by `Character`).
- **Map transition:** old world freed → owners invalid → elements self-`QueueFree` on next pass. No `ChangeMap` changes. ≤1 frame of orphans, behind the loading overlay.
- **Character removed mid-flight:** same `IsInstanceValid` path frees name/bubble/battle text together; in-progress battle text just disappears.
- **Resize mid-bubble/mid-rise:** `ScaleChanged` only on real change; in-place re-layout; 1-frame pop accepted.
- **Scale change with no map:** `ApplyMode` no-ops without `Current` → no event → nothing to re-layout.
- **Gutters:** culling against the display rect; text at the camera-view edge hides at the gutter boundary like the world texture.
- **Rapid bubble spam:** existing replace-`QueueFree` semantics unchanged.
- **Null-safety:** all existing null-checks on `_nameLabel`/`_chatBubble`/`_battleText` stay; only the parent changes.
- **Headless/CI:** pure tests run headless; everything else is a manual checklist (Stage 1 pattern).

**Accepted imperfection:** text whose *owner* is clipped by the gutter can render past the world edge until the text's *own rect* exits the display rect (per-rect culling, not pixel-composited with the world). Called out in the manual checklist, not engineered around.

## 5. Testing & verification

**xUnit** (`WorldTextProjection`, compiles under GodotSharp 4.6.2):
- `Project`: identity (camera at origin, S=1); camera offset (S=2 → 100 world-px from center lands 200 display-px from origin); fractional camera offset; **inverse-property test** — `WindowToWorld`'s math composed with `Project` round-trips its input (guards against sign/order slip between the two transforms).
- `IsCulled`: four edges — inside → false; touching boundary → false (inclusive); just past each edge → true.

**Manual checklist** (live server, Stage 1 Task-6 shape):
1. **Crispness (the point):** at 1080p (S=2) and 4K (S=3) names/bubbles/battle text pixel-crisp vs Stage 1's soft upscale; on-screen *size* unchanged (12 world-px → 24/36 screen px); 1× mode → 12px = today's in-world rendering.
2. **Tracking:** walk + camera lerp — text glued, no per-frame lag (the priority-100 `_Process` decision; engine contract covered by `tools/tests/text_bridge_order.gd`), crisp during sub-pixel camera scroll.
3. **Bubbles:** long-message wrap proportional at 2×/3×; anchored above nameplate, centered, incl. long names.
4. **Battle text:** spread/rise unchanged; 1s free.
5. **Ordering:** bubble > name > world; battle text below name (as today).
6. **Input parity:** click on visible bubble → world click fires; HUD clicks and chat typing unaffected.
7. **Resize/mode:** no-scale-change drag (1600×900 ↔ 1920×1080) repositions only, no re-measure stutter; force an S change (1× ↔ 2× toggle) with a bubble up and battle text rising → re-layouts, no crash, ≤1-frame pop.
8. **Lifecycle:** second map entry → no orphaned old-map text; character death → its text gone within a frame.
9. **Culling:** odd-sized window (1921×1081), text hidden in the gutter, reappears on re-entry.
10. **Regression:** full Stage 1 checklist (I1–I7) still passes — especially I6 input and I7 lifecycle.

**Scope:** single plan, ~5–6 tasks (projection helper + tests; bridge node + ordering probe + viewport event + wiring; name label migration; bubble migration; battle text migration; manual verification). No split.
