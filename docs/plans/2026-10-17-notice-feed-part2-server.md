# Notice Feed — Part 2: Server Implementation Plan

**Goal:** Add the `NTF` notice packet, move quest/buff/craft messages onto it, and add quest-item pickup credit.

**Architecture:** A `NoticeType` enum and a `P.Notice(type, text)` builder. Existing `world.Send(player, …)` call sites swap their builder; no routing changes. Pickup credit is a new `Player.PickedUpItem` hook next to `Killed`/`TalkedTo`. It reuses `QuestStateResolver.IsActive` and the quest window's item-family totals, so the counts always match the quest window.

**Tech Stack:** .NET 10 (Goose server), xUnit (`Goose.Tests`).

Design: `docs/plans/2026-10-17-notice-feed-design.md` (client repo). This is **Part 2 of 2**; Part 1 (client) is `2026-10-17-notice-feed-part1-client.md`.

**This part edits the sibling repo `../illutiagooseserver`, not the client worktree.** Its `master` has unrelated uncommitted changes (`WanderingChest*`), so leave them alone and work in a worktree:

```bash
git -C ../illutiagooseserver worktree add .worktrees/feat-notice-feed -b feat-notice-feed
```

Baseline: `dotnet test Goose.Tests` in that worktree must be green before Task 1.

---

## APIs verified

- `P` is `public static class P` in `Goose/Packets.cs:5`; chat builders `:36-41` (`ServerMessage = "$7" + message`). `BattleTextYellow` `:383-386` (`"BT" + LoginID + ",60," + message`).
- Quest kill/talk credit: `Player.UpdatePossibleQuestProgress` `Goose/Player.cs:1288-1306`; credit send `:1297-1300`, gated by `QuestCreditFilterEnabled` (`:422`, bit `ToggleSetting.QuestCredit = 8` `:412`). Entry points `Killed` `:1278`, `TalkedTo` `:1283`.
- Buff text sends: `Player.cs:2449-2450` (OnEffectText in `AddBuff(Buff, GameWorld, bool, bool)` `:2331`), `:2487-2488` (Off/On in `RenewBuff` `:2472`), `:2670` (OffEffectText in `RemoveBuff(Buff, GameWorld, bool, bool)` `:2613`).
- Quest reward: `QuestRewardGranter.Give` `Goose/Quests/QuestRewardGranter.cs:5`, send `:143-144`.
- Abandon: `Goose/Quests/AbandonConfirmWindow.cs:47`.
- Craft success: `Goose/Inventory.cs:1401`.
- Pickup: `Goose/Events/PickupItemEvent.cs`. Success branch `if (this.Player.Inventory.AddItem(...))` at `:111`, log call `:119-121`. Gold is handled earlier (`:79-86`) and must not trigger credit.
- Active-quest rule: `QuestStateResolver.IsActive(Quest, Player)` `Goose/Quests/QuestStateResolver.cs:13-20` (`internal static`). A completed non-repeatable quest stays in `QuestsStarted`, and this excludes it.
- Family totals: `QuestWindow.GetItemFamilyTotalQuantities(Quest, GameWorld)` `Goose/Quests/QuestWindow.cs:438-453`, currently `private static`; skips null templates and sums `Value2` per `BaseTemplateID`. Family count: `Inventory.GetInventoryItemFamilyCount(int)` `Goose/Inventory.cs:954-963` (excludes `Custom` items). `ItemTemplate.BaseTemplateID` `Goose/ItemTemplate.cs:62`.
- Test harness: `TestWorldFixture` (`TestSupport/TestWorldFixture.cs:8`), `CapturingPlayer.Sent` `:101-106`, `CommandPlayerOn(map,x,y)` `:108`, `AddBaseSpellEffect` `:82`. Buff precedent `Goose.Tests/BuffNullGuardTests.cs:8-17`. Pickup end-to-end precedent `Goose.Tests/ItemScriptHookTests.cs:150-183` (settings pin `ItemIDStartpoint`/`InventorySize` `:19-25`). Item-family setup precedent `Goose.Tests/QuestWindowItemRequirementTests.cs:14-70`.
- Existing assertions that keep passing (substring `Contains`): `QuestAbandonCommandTests.cs:100,115,158`; `Quests/QuestRewardGranterTests.cs:41,58,70`; `CombineBagTests.cs` "Successfully created" negatives.

---

### Task 1: `NoticeType` + `P.Notice`

**Files:**
- Create: `Goose/NoticeType.cs` (namespace `Goose`)
- Modify: `Goose/Packets.cs:41` (add after `TellMessage`)
- Test: `Goose.Tests/NoticePacketTests.cs`

```csharp
public enum NoticeType
{
    Info = 0,
    Quest = 1,
    BuffOn = 2,
    BuffOff = 3,
}
```

The ids are a wire contract mirrored by the client's `NoticeType` in `Scripts/Constants.cs`. A one-line comment saying so is justified under AGENTS.md (wire format).

```csharp
public static Func<NoticeType, string, string> Notice = (type, message) => { return "NTF" + (int)type + "," + message; };
```

**Tests:** `P.Notice(NoticeType.Quest, "a, b")` == `"NTF1,a, b"`; one assert per enum value pinning the id (`Info`→`NTF0,`, `BuffOff`→`NTF3,`). These fail if anyone renumbers the enum.

Red (compile) → implement → green: `dotnet test Goose.Tests --filter FullyQualifiedName~NoticePacket`. Commit `feat(notice): NTF packet builder`.

---

### Task 2: Move quest messages to `Notice(Quest)`

**Files:**
- Modify: `Goose/Player.cs:1299`, `Goose/Quests/QuestRewardGranter.cs:144`, `Goose/Quests/AbandonConfirmWindow.cs:47`
- Test: new `Goose.Tests/QuestCreditNoticeTests.cs`; tighten `QuestAbandonCommandTests.cs:100`, `Quests/QuestRewardGranterTests.cs:41`

Swap builders only; the text is unchanged:
- `P.BattleTextYellow(this, $"Quest Credit: …")` → `P.Notice(NoticeType.Quest, $"Quest Credit: …")`. Keep the `!QuestCreditFilterEnabled` gate.
- `P.ServerMessage(rewardMessage)` → `P.Notice(NoticeType.Quest, rewardMessage)`.
- `P.ServerMessage($"Abandoned quest: …")` → `P.Notice(NoticeType.Quest, …)`.

**Mutation impact:** only the outgoing packet changes. No server-side state, persistence or derived state is affected, and the client is the sole reader.

**Tests:**
- `Killed_WithKillProgress_SendsQuestNotice`: player has a `QuestProgress` for a Kill requirement (template id 5, `Value2 = 3`). `Killed(npc5)` → `Sent` contains `P.Notice(NoticeType.Quest, "Quest Credit: Q (1/3)")`. **Adversarial:** `Sent` contains no entry starting with `"BT"` (fails if the old battle text is kept alongside).
- `Killed_WithQuestCreditFilter_SendsNothing`: `ToggleSettings |= ToggleSetting.QuestCredit` → no `NTF` sent, but progress still increments to 1.
- Tighten `QuestAbandonCommandTests.cs:100` to `s.StartsWith("NTF1,Abandoned quest: Quest Two")`, and `QuestRewardGranterTests.cs:41` to `s.StartsWith("NTF1,[Quest Reward]: 500 gold")`. Both fail red before the swap.

Commit `feat(notice): send quest credit, rewards and abandons as notices`.

---

### Task 3: Move buff and craft messages

**Files:**
- Modify: `Goose/Player.cs:2450,2487,2488,2670`, `Goose/Inventory.cs:1401`
- Test: new `Goose.Tests/BuffNoticeTests.cs`; tighten one success assertion in `CombineBagTests.cs` (or add one beside the existing success-path test)

Swaps:
- OnEffectText (`:2450`, `:2488`) → `P.Notice(NoticeType.BuffOn, …)`.
- OffEffectText (`:2487`, `:2670`) → `P.Notice(NoticeType.BuffOff, …)`.
- `"Successfully created " + result.Name + "."` → `P.Notice(NoticeType.Info, …)`.

The `!= ""` guards stay, so an empty effect text still sends nothing.

**Tests** (`TestWorldFixture`, `CommandPlayerOn`, `AddBaseSpellEffect` with `OnEffectText = "You feel strong."`, `OffEffectText = "You feel weak."`, `Duration = 120`):
- `AddBuff_SendsBuffOnNotice` → `Sent` contains `P.Notice(NoticeType.BuffOn, "You feel strong.")`, and no entry starts with `"$7You feel strong."`.
- `RemoveBuff_SendsBuffOffNotice` → `BuffOff` notice. Note `:2670` sits inside `if (this.State == States.Ready)`, so set `player.State = Player.States.Ready`.
- `AddBuff_SameEffectRenew_SendsOffThenOn` — adversarial for `RenewBuff`: casting the same effect again goes through `RenewBuff`. Assert the BuffOff notice index < the BuffOn notice index (fails if the categories are swapped). If `AddBuff` doesn't route to `RenewBuff` in the fixture, find the call site of `RenewBuff` (`grep -n "RenewBuff(" Goose/Player.cs`) and drive that path. Do not drop the test.
- Empty `OnEffectText` → no `NTF2` sent.
- Craft: the existing successful-combine test in `CombineBagTests.cs` should assert `Sent` contains `P.Notice(NoticeType.Info, "Successfully created X.")`.

Commit `feat(notice): send buff and craft-success text as notices`.

---

### Task 4: Quest-item pickup credit

**Files:**
- Modify: `Goose/Quests/QuestWindow.cs:438` (`private static` → `internal static`)
- Modify: `Goose/Player.cs` (add `PickedUpItem` after `TalkedTo`, `:1283-1286`)
- Modify: `Goose/Events/PickupItemEvent.cs` (call it in the `AddItem` success branch, after the log call at `:119-121`)
- Test: new `Goose.Tests/QuestItemPickupNoticeTests.cs`

**Contract:** `internal void PickedUpItem(Item item, GameWorld world)`:
- Sends packets only; it mutates no quest, progress or inventory state.
- Precondition: the item is already in the inventory (count is read after the add).
- Returns immediately if `QuestCreditFilterEnabled` or `item.Custom` (custom items never count toward a family, per `Inventory.cs:959`).
- `family = item.Template.BaseTemplateID`. For each `quest` in `QuestsStarted` (snapshot with `.ToList()`; distinct by `Id`) where `QuestStateResolver.IsActive(quest, this)`, and `QuestWindow.GetItemFamilyTotalQuantities(quest, world)` contains `family` → `required`, send `P.Notice(NoticeType.Quest, $"Quest Credit: {quest.Name} ({Inventory.GetInventoryItemFamilyCount(family)}/{required})")`.
- Format matches the kill credit at `Player.cs:1299` exactly (no item name; design decision).

**Mutation impact:** none server-side; read-only over `QuestsStarted`, `QuestsCompleted`, inventory and templates. Quest icons are not refreshed on pickup. That is existing behavior and out of scope.

**Tests** (setup combines `ItemScriptHookTests` pickup scaffolding with `QuestWindowItemRequirementTests` family templates: base `Herb` 100, variant `Herb I` 1001; quest "Herb Quest" with Item requirement `Value = 100, Value2 = 5`; player `State = Ready`, standing on an `ItemTile` with `PickupTime = 0`). Drive via `new PickupItemEvent { Player = player }.Ready(world)`.

| Invariant | Test |
|---|---|
| Active quest gets credit with post-add count | `Pickup_ActiveQuest_SendsCreditWithNewCount` — inventory already holds 2 Herb, pick up 1 → `"NTF1,Quest Credit: Herb Quest (3/5)"` (**adversarial:** `(2/5)` means the count was read before the add) |
| Variant counts toward base family | `Pickup_VariantItem_CountsTowardFamily` — pick up `Herb I` stack 2 → `(2/5)` |
| Past requirement still shown | `Pickup_OverRequirement_ShowsOverCount` — holds 5, pick up 1 → `(6/5)` |
| Duplicate requirement rows sum | quest with two rows for 100 (`Value2` 2 and 3) → `/5`, one line |
| Not started → nothing | quest not in `QuestsStarted` → no `NTF` |
| Completed non-repeatable → nothing | in both `QuestsStarted` and `QuestsCompleted` → no `NTF` |
| Filter on → nothing | `ToggleSettings |= QuestCredit` → no `NTF`, item still picked up |
| Unrelated item → nothing | pick up a non-quest template → no `NTF` |
| Two quests share family → one line each | two active quests needing Herb → exactly two `NTF1` lines with their own names |
| Failed pickup → nothing | inventory full (`InventorySize` pinned small and filled) → no `NTF` |

Red (compile/`private` access) → implement → green: `dotnet test Goose.Tests --filter "FullyQualifiedName~QuestItemPickupNotice|FullyQualifiedName~QuestWindowItemRequirement"` (the second filter guards the visibility change). Commit `feat(quests): quest credit notice on quest-item pickup`.

---

### Task 5: Full suite + end-to-end smoke

1. `dotnet test Goose.Tests` in the server worktree: all green.
2. Run the server locally from the worktree and the client from the client worktree with `GOOSE_HOST=127.0.0.1 ./run.sh` (env var hook in `run.sh`).
3. Check in game:
   - Kill a quest NPC → yellow `Quest Credit: …` line below the minimap, no yellow battle text above your head.
   - Pick up a quest item → `Quest Credit: Quest (n/m)`.
   - Cast a buff with on/off text → green line, then a grey line on expiry. Neither appears in chat.
   - Craft a recipe → white "Successfully created …" line.
   - Trigger more than 6 notices fast → the oldest drops, newest stays at the bottom, each fades after ~4 s.
   - Toggle the minimap off in Options → the feed jumps up to the top (y≈28 at 1×). Change UI scale → the feed stays right-aligned with the minimap.
   - `/toggle quest` → no credit lines.
4. Report results; a visual problem goes back to Part 1's Task 3.

No commit unless fixes were needed.

---

## Final review notes

- Threading: every changed site runs inside world event handling (same context as today's sends); no new threads.
- Persistence: none. No schema, no quest-state change.
- Compatibility: pre-feature clients drop `NTF` (accepted in the design). Ship the server and client together.
- Design alignment: categories/ids, `Quest Credit: {Quest} ({count}/{required})` wording, filter behaviour, floor-pickup-only, craft failures stay in chat, XP/"Level Up!" battle text untouched. All match the design doc.
