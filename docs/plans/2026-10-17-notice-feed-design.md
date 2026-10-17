# Notice Feed — Design

A floating, temporary message feed on the right side of the HUD, directly below the minimap.
The server sends short notices on a new `NTF` packet; each line shows for a few seconds and
fades. Some existing chat lines and quest-credit battle text move here, and a new quest-item
pickup credit line is added.

## Wire protocol

`NTF<category>,<text>` — server → client. `category` is an integer; `text` is everything after
the first comma (commas in text are safe).

`NoticeType` enum, mirrored on client and server. The client owns the color:

| id | Name | Color | Used by |
|---|---|---|---|
| 0 | Info | white | "Successfully created X." |
| 1 | Quest | yellow (battle-text Yellow60) | quest credit (kill/talk/item), quest reward, abandoned quest |
| 2 | BuffOn | light green | spell `OnEffectText` |
| 3 | BuffOff | grey | spell `OffEffectText` |

- Unknown category ids render with the Info color.
- Pre-feature clients register no `NTF` handler and drop the packet, so they lose the migrated
  messages. Accepted: server and client ship together.

## Client

New pieces:

- `NoticePacket` — parses `NTF`.
- `NoticeType` enum + color mapping.
- `NoticeFeedModel` — pure C#: `Add(type, text)`, `Tick(delta)` → expired entries; enforces
  the cap and lifetime. Unit tested.
- `NoticeFeed : Control, IScalableWindow` — code-built like `MinimapControl`, added in
  `GameHud` right after the minimap.

Layout (1× canvas, scales with UI scale):

- Plain floating text — no frame, no background. Black outline like the minimap name label.
- Block 240 px wide; its right edge lines up with the minimap's right edge. Text is
  **left-aligned**; long lines wrap.
- Top sits just below the minimap name label (~y=182). If the minimap is hidden, the feed moves
  up to the minimap's top (y=28).
- Mouse ignored; HUD windows draw over it.

Behaviour:

- **Newest at the bottom**; older lines sit above it.
- At most **6** entries; a 7th evicts the oldest. (Start at 6 and tune.)
- Each entry lives 5 s: 4 s solid, 1 s fade-out.
- No merging, no option toggle, no history. It survives map changes and is cleared with the HUD.

## Server (`../illutiagooseserver`)

- `Goose/NoticeType.cs` enum; `P.Notice(type, text)` → `"NTF" + (int)type + "," + text`.
- Migrations (all are `world.Send(player, …)` to self):
  - Quest kill/talk credit (`Player.UpdatePossibleQuestProgress`): `BattleTextYellow` →
    `Notice(Quest)`, same text, still muted by `/toggle quest`.
  - Buff on/off effect text (`Player.cs` OnEffectText/OffEffectText sends) → `BuffOn`/`BuffOff`.
  - Quest reward message (`QuestRewardGranter`) and "Abandoned quest" (`AbandonConfirmWindow`) →
    `Quest`.
  - "Successfully created X." (`Inventory` combine) → `Info`. Craft *failure* lines stay in chat.
  - XP and "Level Up!" battle text stay as they are.
- New quest-item pickup credit: after `Inventory.AddItem` succeeds in `PickupItemEvent`, for each
  quest the player has started and not completed that has an Item requirement in the picked
  item's family (`BaseTemplateID`), send
  `Notice(Quest, "Quest Credit: {Quest.Name} ({count}/{required})")` — the same wording as
  kill credit.
  - `count` = inventory family count after the add; `required` = summed family requirement
    (same rule as the quest window, extracted into a shared helper).
  - One line per quest per pickup; still shown past the requirement (6/5); muted by
    `/toggle quest`.
  - Only floor pickups fire it — trade, vendor, crafting, bank and rewards do not (accepted).

## Testing

- Client: `NoticePacket` parse tests (incl. commas in text, empty text, unknown category);
  `NoticeFeedModel` tests (cap/eviction order, lifetime/fade, newest-at-bottom ordering).
  Feed rendering is verified by running the game.
- Server (`Goose.Tests`): `P.Notice` format; each migrated site emits `NTF` instead of its old
  packet; pickup credit — matching started quest, not-started quest, completed quest, non-quest
  item, filter on, two quests sharing an item family.
