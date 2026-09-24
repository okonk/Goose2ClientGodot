# Chat Window: Tabs, Move, Resize — Design

Date: 2026-09-24
Status: Approved

## Goal

Give the chat window filter tabs (with per-recipient tell tabs), let the player move it by
dragging the tab strip, and let them resize it from any edge or corner. Position, size,
visibility and tab configuration persist per character.

## Current state

- `ChatWindow` (`Scripts/UI/ChatWindow.cs`) is a plain `Control`, not a `BaseWindow`: fixed
  500×208 at the bottom-left, baked `Assets/UI/chat.png` background, one `RichTextLabel`
  log and a `LineEdit` input.
- UI scaling works by `UiScaleLayout.Snapshot` of 1× offsets multiplied by the factor, which
  assumes fixed window sizes.
- `CharacterSettings.WindowSettings` already stores Position, Size, Factor, CanvasSize, Placed.
- Server (`illutiagooseserver/Goose/Commands/TellCommand.cs`) echoes outgoing tells to the
  sender as a Tell-type message `"[tell to] <Name>: <text>"`; incoming tells arrive as
  `TellPacket` with the sender name. Failure messages (`"<Name> is not online."`) arrive as
  Tell-type messages with no prefix; AFK / tells-disabled notices arrive as server messages.

## Approach

ChatWindow becomes a `BaseWindow` subclass; `BaseWindow` gains opt-in resizing; tab and
routing logic lives in a pure, unit-tested `ChatLog` model. ChatWindow is a thin view.

## 1. Tab model and routing (`Scripts/UI/ChatLog.cs`, pure C#)

Ordered tabs, each with kind (All, Guild, Group, Chat, System, Tell), label, a line buffer
capped at 500 lines, and an unread flag.

`Add(message, ChatType, tellName?)` routes a line:

| Line | Tabs |
|---|---|
| any | All (always) |
| `ChatType.Guild` | Guild |
| `ChatType.Group` | Group |
| `ChatType.Chat` (ChatPacket, HashMessage) | Chat, if enabled |
| `ChatType.Server` / `Client` | System, if enabled |
| `TellPacket` from Bob | Tell:Bob (created if missing) |
| Tell-type message `[tell to] Bob: …` | Tell:Bob (created if missing) |
| Tell-type message with no recognised prefix | All + active tab if it is a tell tab |
| Melee / Spells | All only |

- Tell names match case-insensitively; label keeps the first-seen casing.
- Appending to a non-active tab sets unread; activating clears it. All never shows unread.
- Closing a tell tab drops it and its buffer; if it was active, All becomes active.
- Order: All, enabled fixed tabs (Guild, Group, Chat, System), then tell tabs in open order.
- Existing escaping (`[` → `[lb]`, backtick → ♥) and colour BBCode move into the model;
  buffers hold formatted BBCode so a tab switch just joins lines into the single log label.

**Sending.** `ApplyChannel(text)` runs before `ChatCommandParser`. Text starting with `/`
passes through. Otherwise: Guild → `/guild `, Group → `/group `, Tell:Bob → `/tell Bob `,
All/Chat/System → unchanged (say). The input placeholder shows the channel ("Guild",
"Tell Bob"). Existing hotkey prefills (Enter, `/`, guild, tell, R reply) are unchanged.

## 2. Window, move and resize

Scene (`Scenes/UI/ChatWindow.tscn`):

```
ChatWindow (BaseWindow, WindowName="Chat", Resizable)
├─ Background   (themed WindowPanel, full rect; chat.png dropped)
└─ Content      (VBoxContainer, full rect, small margin)
   ├─ TabStrip  (HBox: tab buttons + expanding filler = drag handle)
   ├─ ChatLog   (RichTextLabel, expand-fill)
   └─ Input     (LineEdit)
```

- No `TitleBar` node → no title bar or close button; the `ToggleChat` hotkey shows/hides.
- `MakeDragHandle` on the strip's filler only, so tab clicks select tabs.
- Tabs are toggle `Button`s in a `ButtonGroup`, `FocusMode.None`; tell tabs carry a small ×.
  Unread tabs use a highlight theme variation.
- Hover fade uses `BaseWindow`'s rect check (replaces the Panel MouseEntered/Exited hack).
- `BaseWindow.Toggle` persists visibility, so hiding chat now survives relog (intended).

**Resizing in `BaseWindow`** (`protected virtual bool Resizable => false`; chat overrides):

- Eight transparent handles (4 edges ~5px, 4 corners ~10px), kept topmost, with matching
  resize cursors.
- Dragging adjusts Position and Size together; left/top move the origin. Clamped to
  `MinResizeSize` (chat 260×110 logical, scaled) and the canvas.
- Release saves through the same `SetWindowSetting(..., Position, Size, Factor, ...)` as
  drag; `CancelDrag` restores the pre-resize rect.
- Rect math lives in a pure helper `WindowResize.Apply(rect, edge, delta, minSize, canvas)`.

**Scaling a resizable window.** Root size = saved `ws.Size / ws.Factor × currentFactor`
(tscn 500×208 when unsaved). Container children reflow; only font sizes, tab strip height and
the min size scale. Children are marked with `UiScaleLayout.SkipMeta` so the snapshot does
not own their offsets. Placement still uses `WindowPlacement.ResolveScaled` with the saved
size. Default position (8, 507) on the 1280×720 design canvas is added to
`DefaultWindowLayout`.

Non-resizable windows are unaffected.

## 3. Persistence

- Position / size / visibility: existing `WindowSettings` under `"Chat"`.
- New `CharacterSettings.ChatTabs` (`List<string>` of enabled fixed tabs, e.g.
  `["Guild","Group"]`). Missing field (old saves) → Guild + Group. All is implicit.
- Right-click on the tab strip opens a `PopupMenu` with checkboxes Guild, Group, Chat,
  System. Changes apply immediately and save. Disabling the active tab switches to All; a
  re-enabled tab starts empty.
- Tell tabs, active tab and history are not persisted.
- Options → reset window positions covers chat via `ResetToDefault`; it does not reset tabs.

## 4. Testing

- `tests/Goose2Client.Tests/ChatLogTests.cs`: routing per ChatType, optional tabs on/off,
  tell tabs from incoming and `[tell to]` echo (case-insensitive), unprefixed Tell-type
  routing, unread set/clear and All exempt, closing active tell tab → All, 500-line cap,
  `ApplyChannel` per tab kind and `/` override.
- `WindowResize` tests: each edge/corner, min size, canvas clamp.
- `WindowPlacementTests`: resized saved size scaled by factor.
- Update `UiScaleSelfTest` expectations for the chat window.
- Manual: drag by filler, resize all 8 handles, change UI scale while resized, relog restores
  position/size/tabs, send and receive tells (tabs + unread), per-tab sending.

## Deferred / accepted gaps

- No Combat tab (Melee/Spells visible in All only).
- No keyboard shortcut to cycle tabs.
- `WindowPlacement.HotbarDefault` still assumes chat at its default spot
  (`ChatClearanceX = 520`); only affects an unplaced hotbar. Left as is.
- No tab reordering.
