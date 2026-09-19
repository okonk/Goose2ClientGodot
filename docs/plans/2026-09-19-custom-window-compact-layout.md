# Compact Custom Window Layout Implementation Plan

**Goal:** Refine the custom-item window within its original 440×260 footprint using a smaller picker, tighter RGBA spacing, a 144-pixel-wide 3× preview, and a labeled name field.

**Architecture:** Preserve the existing scene hierarchy and all behavior code. Update the pure fixed-scale preview metric and the authored `.tscn` geometry only; global UI scaling continues to use the existing `BaseWindow` snapshot and relayout path.

**Tech Stack:** Godot 4 C#, Godot scene resources (`.tscn`), xUnit, .NET 8

---

## APIs verified

- `CustomPreviewMetrics.Scale(Vector2)` owns the common fixed scale, and `Layout(Vector2, Vector2)` calculates every preview layer's final geometry: `Scripts/UI/CustomPreviewMetrics.cs:8-21`.
- `CustomPreviewControl.Refresh()` applies that geometry directly to each layer's `Size` and `Position`: `Scripts/UI/CustomPreviewControl.cs:52-132`.
- Preview layers already use nearest-neighbor filtering: `Scripts/UI/CustomPreviewControl.cs:18-30`.
- Every interactive custom-window node is resolved through a direct `Content/*` path: `Scripts/UI/CustomWindow.cs:55-90`.
- `CustomWindow.Relayout()` applies scaled scene geometry before recalculating preview layers and color-picker cursors: `Scripts/UI/CustomWindow.cs:114-120`.
- The scene already defines the 440×260 shell, full-rect background, title bar, and direct `Content` child hierarchy: `Scenes/UI/CustomWindow.tscn:37-90`.
- Existing slot dimensions and captions are authored in `Scenes/UI/CustomWindow.tscn:92-170`; picker and RGBA geometry is authored at `Scenes/UI/CustomWindow.tscn:182-343`.
- Name and Create behavior depends on the existing `NameField` and `CreateButton` nodes, so their names and parent paths must remain unchanged: `Scenes/UI/CustomWindow.tscn:345-359`, `Scripts/UI/CustomWindow.cs:87-92`.

### Task 1: Change the shared preview scale to 3×

**Files:**
- Modify: `tests/Goose2Client.Tests/CustomPreviewMetricsTests.cs:9-80`
- Modify: `Scripts/UI/CustomPreviewMetrics.cs:8-21`

**Mutation impact:**
- Source of truth changed: `CustomPreviewMetrics.Scale()` in `Scripts/UI/CustomPreviewMetrics.cs:8-12`.
- Important readers: `CustomPreviewMetrics.Layout()` and `CustomPreviewControl.Refresh()` at `Scripts/UI/CustomPreviewMetrics.cs:14-21` and `Scripts/UI/CustomPreviewControl.cs:52-132`.
- Derived/cached state affected: visible preview-layer `TextureRect.Size` and `TextureRect.Position`; no persistent state or cache changes.
- Required propagation sequence:
  1. `Scale()` returns 3 for a positive preview size and 0 for an invalid size.
  2. `Layout()` multiplies every frame by the same scale and retains the current ground-line calculation.
  3. `CustomPreviewControl.Refresh()` assigns the returned geometry to every layer.
  4. `CustomWindow.Relayout()` repeats the calculation after global UI-scale geometry is applied.
- Invariants to preserve:
  - Every layer shares one fixed scale.
  - Invalid controls still return zero scale and zero layout.
  - The existing ground-line relationship remains unchanged.
  - The designed 144×166 viewport fits a standard 48×48 frame horizontally and vertically.
  - Tall frames may exceed the viewport vertically by design.
- Observable proof required: unit tests assert final scale, size, position, and accepted tall-frame overflow.

**Step 1: Update the unit tests first**

Change existing 2× expectations to 3× and add a designed-viewport regression using `new Vector2(144f, 166f)`:

- Positive controls return 3; zero-width or zero-height controls return 0.
- 48×48 and 48×96 layers both use 3×.
- A 48×48 frame in the designed viewport is 144×144, has X position 0, and remains vertically within the viewport.
- A 48×96 frame remains horizontally contained but extends vertically outside the designed viewport.
- A 24-pixel-wide frame remains horizontally centered.
- A zero-size control returns zero size and position.

The designed-viewport test is the adversarial regression: it fails if the scene allocates less than 144 pixels or if scale becomes frame-dependent.

**Step 2: Run the focused tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore --filter FullyQualifiedName~CustomPreviewMetricsTests -v minimal
```

Expected: failures report the current 2× sizes and positions instead of 3×.

**Step 3: Implement the minimal scale change**

Change the valid return value in `CustomPreviewMetrics.Scale()` from `2.0f` to `3.0f`. Do not change the ground-line formula, introduce adaptive scaling, or add comments.

**Step 4: Run focused and project tests**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore --filter FullyQualifiedName~CustomPreviewMetricsTests -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore -v minimal
```

Expected: all tests pass.

**Step 5: Commit**

```bash
git add Scripts/UI/CustomPreviewMetrics.cs tests/Goose2Client.Tests/CustomPreviewMetricsTests.cs
git commit -m "fix(ui): enlarge custom character preview"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| All layers use fixed 3× | Updated common-scale test |
| Designed viewport fits standard width | Designed 144×166 viewport regression |
| Tall clipping remains an explicit compact-layout tradeoff | Designed tall-frame overflow test |
| Invalid controls produce no geometry | Zero-control tests |

### Task 2: Tighten the scene within 440×260

**Files:**
- Modify: `Scenes/UI/CustomWindow.tscn:37-359`

**Mutation impact:**
- Source of truth changed: authored control offsets in `Scenes/UI/CustomWindow.tscn`.
- Important readers: direct node lookups in `Scripts/UI/CustomWindow.cs:55-90`, geometry capture in `Scripts/UI/BaseWindow.cs:47-57`, and global scale application in `Scripts/UI/BaseWindow.cs:101-118`.
- Derived/cached state affected: `BaseWindow` captures the revised child geometry when the scene enters the tree; no persisted schema changes and no business state changes.
- Required propagation sequence:
  1. Godot instantiates the unchanged 440×260 root and full-rect background.
  2. `CustomWindow._Ready()` resolves unchanged direct node paths and wires existing events.
  3. `ScaleRegister()` snapshots revised authored geometry and applies the current global UI scale.
  4. `CustomWindow.Relayout()` recalculates preview layers and picker cursors from the scaled control sizes.
- Invariants to preserve:
  - Root, title bar, and background remain 440×260-compatible.
  - Existing interactive nodes retain names and remain direct children of `Content`.
  - The background asset and shared theme are unchanged.
  - Swatch is exactly 112×112; hue and lightness bars are exactly 112 pixels wide.
  - Preview is exactly 144 pixels wide with clipping retained.
  - Slots remain 32×32 and interactive.
  - RGB maximum remains 255, alpha maximum remains 200, and initial values remain unchanged.
  - `NameField.max_length` remains 255 and Create remains centered below the separator.
- Observable proof required: clean build/tests plus an in-game screenshot and interaction smoke check.

**Step 1: Preserve the shell and footer separator**

Leave the following unchanged:

- Root offsets `0,0–440,260`.
- Full-rect `Background` using `quest.png`.
- Title bar, title label, and close-button geometry.
- Create footer band below the background's separator.

Do not add panels, textures, style overrides, or section headings.

**Step 2: Resize and align the color block**

Author the compact left column:

- `Swatch`: approximately `(12,30)–(124,142)`, exactly 112×112.
- `HueBar`: approximately `(12,148)–(124,160)`.
- `LightBar`: approximately `(12,166)–(124,178)`.
- Preserve cursor sizes, textures, stretch modes, and child paths.

The three controls must share identical horizontal bounds.

**Step 3: Tighten RGBA rows and source slots**

Use a center control column around `x=136–282`:

- Channel labels around `x=136–150`.
- Equal-width sliders around `x=152–252`.
- Right-aligned value labels around `x=256–282`.
- Row tops near 30, 52, 74, and 96, retaining 18-pixel row heights.

Place the slots below the rows:

- Keep each slot 32×32.
- Center the pair with an eight-pixel gap, approximately at `x=173–205` and `x=213–245`.
- Place Graphic and Stats captions directly below their slots with equal caption bounds and centered alignment.

Small pixel adjustments are allowed after the runtime screenshot, but rows must remain equal and no control may overlap.

**Step 4: Allocate the 3× preview and labeled name row**

- Move `Preview` to approximately `(286,24)–(430,190)`, exactly 144 pixels wide, with `clip_contents = true` unchanged.
- Add a non-interactive `NameLabel` as a direct `Content` child around `(12,198)–(48,216)` with text `Name`.
- Move `NameField` to approximately `(50,196)–(430,218)` and preserve `max_length = 255`.
- Keep `CreateButton` centered at `(120,232)–(320,256)` unless a small width reduction improves balance; it must remain fully below the separator.

**Step 5: Run automated verification**

```bash
dotnet build Goose2ClientGodot.csproj --no-restore
dotnet test Goose2ClientGodot.sln --no-restore -v minimal
git diff --check
```

Expected: build and tests pass with no whitespace errors.

**Step 6: Perform the runtime smoke check**

Open the project in Godot 4 and inspect a server-opened Custom window at common global UI scales:

- Confirm the window remains exactly the original footprint.
- Confirm the separator is below the Name row and above Create.
- Drag across the full 112×112 picker and both bars; cursors and RGBA values must remain synchronized.
- Exercise all four sliders and confirm tracks, labels, and values remain aligned.
- Drop, swap, and clear source items; slots and captions must remain readable.
- Inspect a standard character and tall equipment. Standard frames must fit horizontally; tall clipping must stay inside the preview viewport.
- Confirm comma filtering, Create visibility, empty/partial/valid/pending disabled states, submission, close, and reset behavior.

If visual adjustments are necessary, change only `.tscn` offsets and sizes, rerun Step 5, and repeat the screenshot check.

**Step 7: Commit**

```bash
git add Scenes/UI/CustomWindow.tscn
git commit -m "feat(ui): tighten custom window layout"
```

**Invariant-to-proof matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Original footprint and separator are preserved | Runtime screenshot at 440×260 |
| Existing code resolves all controls | Successful scene instantiation and interaction smoke |
| 112-pixel picker remains synchronized | Picker/bar interaction smoke |
| RGBA and slot geometry does not overlap | Runtime screenshot and control smoke |
| 3× standard frame fits horizontally | Metric regression plus runtime preview |
| Existing create workflow remains unchanged | Empty/partial/valid/pending smoke states |
| Shared assets and theme are untouched | Final Git diff |

### Task 3: Final regression and design review

**Files:**
- Verify: `docs/plans/2026-09-19-custom-window-compact-layout-design.md`
- Verify: `Scripts/UI/CustomPreviewMetrics.cs`
- Verify: `tests/Goose2Client.Tests/CustomPreviewMetricsTests.cs`
- Verify: `Scenes/UI/CustomWindow.tscn`

**Step 1: Run final verification**

```bash
dotnet test Goose2ClientGodot.sln --no-restore -v minimal
git diff --check
git status --short
```

Expected: all tests pass, no diff errors remain, and the worktree is clean after implementation commits.

**Step 2: Compare the final screenshot and diff with the design**

Confirm:

- The result reads as a refinement of the original window, not a larger editor.
- Root size is still 440×260.
- Picker and bars are 112 pixels wide.
- RGBA rows use consistent compact spacing.
- Preview is 144 pixels wide at fixed 3×.
- Name and Create sit on opposite sides of the built-in separator.
- No behavior code, protocol code, shared theme, background asset, or unrelated files changed.

**Step 3: Report any unverified runtime item accurately**

If Godot runtime validation is unavailable, report it as outstanding. Passing .NET tests does not prove `.tscn` parsing, separator placement, or visual quality.
