# Key Binding Remapping Design

## Summary

Add a global key-binding editor opened from the in-game Options window. It edits every client gameplay action, supports multiple keyboard, mouse, and gamepad bindings per action, and continues using Godot's `InputMap` as the runtime input boundary.

Bindings are staged until Apply, persisted globally, and restored from the project's action map when reset. Keyboard bindings retain physical key positions while displaying labels from the active keyboard layout.

## Goals

- Open a dedicated bindings window from Options.
- Rebind every client gameplay action without rewriting gameplay around a custom input system.
- Support multiple bindings per action.
- Support physical keyboard keys, modifier chords, non-primary mouse buttons and wheel directions, gamepad buttons, and signed gamepad axes.
- Apply gamepad bindings to any connected controller.
- Persist overrides globally rather than per character.
- Allow unbound actions.
- Warn about behaviorally meaningful conflicts without rejecting them.
- Reset one action or all actions to factory defaults.
- Prevent capture from triggering gameplay.
- Audit input consumers so modifier chords and remapped actions work consistently.

## Non-goals

- Remapping Godot UI, pointer, or tracked-device actions.
- Remapping widget controls, chat history navigation, stack-split modifiers, window dragging, or resizing.
- Remapping primary and secondary world pointer interaction.
- Binding Left Click or Right Click to gameplay actions.
- Per-action deadzone, sensitivity, response-curve, or controller-specific settings.
- Fully controller-driven navigation of the settings UI.
- Importing or exporting binding profiles.

## Architecture

`GameManager` owns one `InputBindingService`. The service is initialized before login input is processed and is the only production component that mutates gameplay entries in `InputMap`.

At startup the service:

1. Verifies every catalog action exists.
2. Captures an immutable factory-default snapshot from `project.godot`'s loaded `InputMap`.
3. Loads and validates global overrides.
4. Resolves missing override entries to factory defaults.
5. Applies the resolved snapshot to `InputMap`.

An explicit `InputActionCatalog` defines each remappable action's stable Godot name, friendly label, category, and conflict roles. This catalog excludes unused aggregate and Godot UI/system actions. Adding a future gameplay action requires adding its metadata to the catalog; tests verify that catalog names are unique and exist in `project.godot`.

`KeyBindingsWindow` is a persistent, initially hidden `BaseWindow` instantiated by `GameHud`. `OptionsWindow` gains a **Key Bindings…** button that opens and raises it. The window obtains immutable snapshots from the service and edits a separate draft. Existing gameplay code continues to read Godot actions.

## Action catalog

### Movement

- `MoveUp` — Move Up
- `MoveDown` — Move Down
- `MoveLeft` — Move Left
- `MoveRight` — Move Right

### Combat & Interaction

- `Attack` — Attack
- `PickUp` — Pick Up
- `ToggleMount` — Toggle Mount

### Windows

- `ToggleInventory` — Inventory
- `ToggleSpellbook` — Spellbook
- `ToggleCharacterWindow` — Character
- `ToggleChat` — Chat

### Hotbar

- `Hotkey1` through `Hotkey9` — Hotbar Slots 1 through 9
- `Hotkey0` — Hotbar Slot 10
- `CycleHotbarPage` — Cycle Hotbar Page

### Chat

- `StartChat` — Start Chat
- `SlashCommand` — Slash Command
- `GuildCommand` — Guild Command
- `TellCommand` — Tell Command
- `ReplyCommand` — Reply

### Targeting

- `TargetUp` — Previous Target
- `TargetDown` — Next Target
- `ConfirmTarget` — Confirm Target
- `TargetHome` — Target Self
- `CancelTarget` — Cancel Targeting

### Emotes

- `EmoteHeart` — Heart
- `EmoteQuestion` — Question
- `EmoteDots` — Dots
- `EmotePoop` — Poop
- `EmoteSurprised` — Surprised
- `EmoteSleep` — Sleep
- `EmoteAnnoyed` — Annoyed
- `EmoteSweat` — Sweat
- `EmoteMusic` — Music
- `EmoteWink` — Wink
- `EmoteTrash` — Trash
- `EmoteDollar` — Dollar

### System

- `RefreshPosition` — Refresh Position
- `ToggleFullscreen` — Toggle Fullscreen

The configured `Move` aggregate is excluded because production code does not consume it. `Navigate`, `Submit`, `Cancel`, `Click`, `MiddleClick`, `RightClick`, `Point`, `ScrollWheel`, and tracked-device actions are excluded as Godot UI/pointer/system actions.

## Binding model

A binding is a normalized discriminated value with one of four kinds:

- Keyboard: physical keycode and Ctrl/Shift/Alt/Meta flags.
- Mouse: button and Ctrl/Shift/Alt/Meta flags.
- Gamepad button: standardized button index.
- Gamepad axis: standardized axis and direction `-1` or `+1`.

Keyboard events are stored and applied through `PhysicalKeycode`. Their display text converts the physical key through `DisplayServer.KeyboardGetLabelFromPhysical` and `OS.GetKeycodeString`, retaining modifiers and falling back to the physical key name when no useful logical label is available.

Ctrl, Shift, Alt, and Meta may modify keyboard or mouse bindings but cannot be standalone bindings. Left Click and Right Click are reserved. Middle Click, additional mouse buttons, and wheel directions are bindable.

Captured controller device IDs are discarded. Runtime gamepad events are created for all controllers.

Each action retains its existing `InputMap` deadzone, currently `0.5`. Axis bindings store only axis and direction.

## Persistence

Overrides live in `user://input-bindings.json`, independently of character settings. The JSON has an explicit format version and an action dictionary. Each present action contains its complete override list. A present empty list means intentionally unbound; a missing action inherits its current factory default.

Only actions whose normalized list differs from factory defaults are written. This lets newly introduced catalog actions receive defaults without migrating existing files.

Loading is strict. The service rejects unsupported versions, unknown action names, unknown binding kinds, out-of-range values, reserved mouse buttons, bare modifiers, duplicate records, and malformed JSON. An invalid file leaves all factory defaults active, is not automatically overwritten, logs a warning, and exposes a recoverable warning to the editor. **Reset All → Apply** replaces it.

Apply normalizes and validates the complete draft, computes overrides, writes a temporary file, atomically replaces the destination, then replaces every catalog action's runtime events. If persistence fails, both the live `InputMap` and the prior file remain unchanged and the window displays the failure.

## Window and editing workflow

The Options scene grows vertically to add **Key Bindings…** beneath **Reset UI Layout**. The new bindings window is resizable, participates in UI scaling and standard window placement, and is treated as a centered dialog on first use. Its geometry remains per-character through existing `BaseWindow` behavior; only the binding data is global.

The window contains:

- A search field.
- A scrollable list grouped by catalog category.
- One action row containing its friendly name, binding chips, **Add Binding**, and **Reset Action**.
- Conflict styling and explanatory tooltips.
- An inline status and error area.
- **Reset All**, **Apply**, and **Cancel** controls.

Search matches action and category labels. Categories with no matching rows disappear. Actions with no bindings display **Unbound**.

Clicking a binding chip captures a replacement. Clicking its remove affordance deletes it. **Add Binding** appends. Duplicate bindings within an action collapse to one normalized value.

Apply saves and activates the draft, establishes it as the new clean baseline, and leaves the window open. Cancel or the close button discards edits since the last Apply and closes the window. Reopening always clones the active snapshot.

Reset Action copies that action from the immutable factory snapshot. Reset All replaces the entire draft with the factory snapshot. Neither takes effect until Apply.

## Capture behavior

Starting capture opens an in-window prompt with instructions and an explicit **Cancel Capture** button.

The initiating mouse press and release are ignored. Capture then accepts:

- A non-echo physical keyboard press, with currently held modifiers.
- A permitted mouse button or wheel event, with currently held modifiers.
- A gamepad button press.
- A gamepad axis after it crosses magnitude `0.75` from neutral.

Bare modifier presses keep capture waiting. Left Click and Right Click show a reserved-pointer message and keep capture waiting. Escape is bindable and does not cancel capture; the visible button is the universal cancellation path.

Before arming capture, the service temporarily removes all catalog events from the live `InputMap`. This prevents captured input from moving, attacking, toggling windows, firing hotbar slots, or changing fullscreen state. Raw capture events are marked handled before GUI dispatch, except clicks targeting **Cancel Capture**.

After accepting or cancelling, suppression remains until the captured key/button is released or the axis returns below the neutral threshold. The active pre-capture snapshot is then restored. Closing or hiding the window and scene teardown use the same restoration path.

## Conflict semantics

Conflicts are warnings and never block Apply. They are evaluated using catalog conflict roles rather than only categories or factory-default pairs.

The contexts are:

- Normal gameplay.
- Spell targeting.
- Global commands, which overlap both contexts.

A shared normalized binding warns only when two actions can produce different behavior in the same active context. Compatible aliases share one role and do not warn. Examples:

- Move Up and Previous Target may share Up Arrow: movement is disabled during targeting, and Move Up acts as the same previous-target command there.
- Start Chat and Confirm Target may share Enter: they are active in different contexts.
- Move Up and Next Target conflict: during targeting they request different target directions.
- Toggle Fullscreen conflicts with a same-binding gameplay action because it is global.

Movement directions participate in targeting as their existing previous/next aliases. Hotbar actions participate in targeting as target confirmation. Catalog metadata represents those secondary roles so warnings match actual behavior.

Binding equality includes kind, physical key or button/axis, axis direction, and exact modifier set. Different modifier chords remain distinct.

## Input audit and integration changes

All gameplay keyboard commands currently enter through `InputMap`. Remaining direct reads are fixed behavior and stay outside the catalog:

- Chat Up/Down history and Escape dismissal.
- Ctrl/Shift stack-split modifiers.
- Widget mouse interaction.
- Window drag and resize mouse state.
- Coordinate-bearing world Left/Right Click interaction.

`GameHud` currently rejects every Alt-modified event to stop Alt+Enter from also starting chat. That blanket guard is incompatible with arbitrary Alt chords. It will be removed. Catalog action checks use exact modifier matching, including fullscreen and targeting event handling, so modified and unmodified bindings remain distinct.

Held action polling for movement, attack, and hotbar also uses exact matching so conflict analysis and runtime behavior agree. Discrete command processing remains suppressed while a `LineEdit` has focus. `GameManager` adds the same text-focus guard for fullscreen; movement, attack, hotbar, and `GameHud` already have equivalent guards.

Primary and secondary mouse buttons remain fixed pointer gestures and are rejected during binding capture. World click routing does not change.

## Failure handling

The service exposes startup load status so the bindings window can show a warning after a bad file was encountered before the HUD existed.

Input suppression is stateful and idempotent. Starting a second capture is rejected, and every completion, cancellation, hide, and teardown path restores exactly the active pre-capture snapshot once.

Applying an invalid draft does not write or mutate `InputMap`. Applying during capture first cancels and restores capture state. A save failure keeps the draft visible so the user can retry or cancel.

Unknown future binding kinds or file versions fail closed to defaults rather than partially applying a profile.

## Testing

### Unit and contract tests

- Catalog action names are unique, categorized, and present in `project.godot`.
- Factory keyboard defaults use physical keycodes.
- Binding normalization and equality cover all four kinds, modifiers, and signed axes.
- Left and right mouse buttons and bare modifiers are rejected.
- JSON round-trips every binding kind and intentional empty action lists.
- Missing actions inherit defaults.
- Invalid versions, actions, values, duplicates, and malformed JSON fail closed.
- Override diffing omits factory-equal lists and preserves intentional unbound actions.
- Atomic persistence failure leaves the prior file and active snapshot unchanged.
- Context-role conflict detection accepts compatible targeting aliases and warns for different roles in an overlapping context.
- Reset Action, Reset All, Apply, Cancel, and reopening produce the expected draft and active snapshots.
- Physical-key display uses an injectable logical-label conversion and physical fallback.
- Input integration contracts verify removal of the Alt blanket guard, exact matching, and text-focus suppression.

### Godot runtime self-test

A command-line-gated runtime self-test exercises:

- Capturing factory defaults from the real `InputMap`.
- Applying keyboard, mouse, gamepad button, and signed-axis events.
- Generic controller device handling.
- Capture suppression and release-delayed restoration.
- Options-to-bindings-window scene wiring.
- UI-scale registration and basic resize/placement behavior.

### Manual smoke test

- Open the window from Options and search each category.
- Add, replace, and remove multiple bindings.
- Bind keyboard chords, middle/extra mouse buttons, wheel directions, gamepad buttons, sticks, and triggers.
- Confirm Left/Right Click and bare modifiers are rejected.
- Confirm equivalent cross-context defaults do not warn and real same-context conflicts do.
- Confirm capture never triggers gameplay and remains suppressed until release/neutral.
- Confirm Apply, Cancel, individual reset, Reset All, and unbound actions.
- Relaunch and switch characters to confirm global persistence.
- Verify logical display labels on a non-QWERTY layout while physical positions remain stable.
- Verify controller axes and triggers on physical hardware.

OS keyboard-layout labels and physical controller drivers remain manual coverage because the headless environment cannot emulate them faithfully.
