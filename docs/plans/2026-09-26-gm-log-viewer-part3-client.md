# GM Log Viewer — Part 3 of 3: Godot Client Implementation Plan

**Status:** Approved design, revised implementation plan only

**Goal:** Implement the Godot GM Log Viewer client against the revised Part 2 protocol: publish frame 29, send explicit Fresh/Page requests, parse deterministic chunked row JSON without narrowing persisted values, stage complete bounded responses, maintain server-issued page-token history, render a resizable searchable table/detail window, copy complete stable details locally, and close through the existing WBC lifecycle.

**Part dependency:** This is **Part 3 of 3**. It depends on revised server Part 1 at `/home/agent/workspace/illutiagooseserver/.worktrees/gm-log-viewer/docs/plans/2026-09-26-gm-log-viewer-part1-server-query.md` and revised server Part 2 at `/home/agent/workspace/illutiagooseserver/.worktrees/gm-log-viewer/docs/plans/2026-09-26-gm-log-viewer-part2-server-integration.md`. Revised Part 2 is authoritative where the older design document differs. Its metadata contract is at lines 78-88, Fresh/Page grammar at lines 90-113, session-token behavior at lines 115-136, result/chunk/JSON contract at lines 138-200, paced delivery at lines 220-228, and client handoff at lines 743-745.

**Scope boundary:** Work only in `Goose2ClientGodot`. Do not change server code, query semantics, descriptor semantics, authorization, session capabilities, audit behavior, or limits. Do not add live tailing, automatic refresh, arbitrary sorting, saved filters, bulk export, or cross-session filter/result persistence.

**Architecture:** Keep wire parsing, response assembly, and search state independent of Godot controls. Task 1 defines every wire-level DTO in `Scripts/Logs/LogWireModels.cs` before implementing packet formatters/parsers. Task 2 consumes those DTOs and owns metadata, draft/applied filters, one active request, ordered chunk staging, visible rows, selection, and page-token history. `LogViewerWindow` projects state into controls and publishes through an injectable query-sender seam. No LRD packet changes visible rows; only a valid matching LRF can commit a fully reconstructed page.

**Repository rule:** All planned production, test, scene, and script changes add no comments or doc strings. Existing unrelated comments remain untouched.

---

## APIs verified against the current client tree

| Fact/API | Verified location |
|---|---|
| Client frame values currently end at `Custom = 28`; frame 29 is free and must be append-only | `Scripts/WindowFrames.cs:3-33` |
| MKW parses frame, window ID, title, five button flags, NPC ID, and trailing IDs | `Scripts/Network/Packets/MakeWindowPacket.cs:6-30` |
| CLW and ENW expose the server window ID | `Scripts/Network/Packets/CloseWindowPacket.cs:6-18`, `Scripts/Network/Packets/EndWindowPacket.cs:6-18` |
| `PacketManager.Listen<T>` has one handler per packet type/prefix, runs observers after parsing, suppresses parse exceptions, and requires explicit removal | `Scripts/Network/PacketManager.cs:7-72` |
| `PacketParser.GetWholePacket()` exposes the complete packet; its numeric conversions are culture-sensitive and do not enforce exact field counts | `Scripts/Network/PacketParser.cs:7-25,35-81` |
| Incoming bytes are ASCII, complete packets enter the main-thread inbox, and disconnect/error publication is marshaled there | `Scripts/Network/NetworkClient.cs:87-108,123-165`, `Scripts/Network/PacketInbox.cs:6-47` |
| `NetworkClient.Send` is nonvirtual, appends `\x1`, and reaches a real socket; existing close flow sends WBC through `WindowButtonClick` | `Scripts/Network/NetworkClient.cs:87-108,277-280` |
| `WindowButtons.Close` has numeric value 2 | `Scripts/WindowButtons.cs:3-10` |
| `BaseWindow` resolves standard chrome and supports resizable minimum/clamped/persisted layout | `Scripts/UI/BaseWindow.cs:11-79,109-125,277-376` |
| The existing single server-window pattern registers in `_Ready`, removes in `_ExitTree`, resets on replacement MKW, handles ENW/CLW, and sends WBC Close locally | `Scripts/UI/CustomWindow.cs:8-13,50-113,253-282,459-464` |
| `GameHud` persistently instantiates fixed windows before managers and exposes typed references | `Scripts/UI/GameHud.cs:10-38,53-114` |
| `GameManager` creates `NetworkClient`/`PacketManager`, drains packets on the main thread, and already has command-line self-test gates | `Scripts/GameManager.cs:89-96,100-120,218-235` |
| Disconnect observers are removed at teardown | `Scripts/GameManager.cs:569-573` |
| `IWindow` requires `WindowFrame` and `WindowId` | `Scripts/UI/IWindow.cs:5-9` |
| Dialog first placement is centralized in `DefaultWindowLayout.IsDialog` | `Scripts/UI/DefaultWindowLayout.cs:7-44` |
| UI scale snapshots geometry/minimum sizes and reflows registered windows | `Scripts/UiScaleLayout.cs:7-79`, `Scripts/UiScaleApplier.cs:132-172` |
| The design viewport is 1280×720 with a 640×360 minimum | `project.godot:23-28` |
| Unit tests compile every `Scripts/**/*.cs` against GodotSharp 4.6.2; production uses Godot .NET SDK 4.7.2/net8 | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:1-21`, `Goose2ClientGodot.csproj:1-18` |
| Existing packet and resize tests can exercise handlers/layout without a running Godot tree | `tests/Goose2Client.Tests/CustomWindowPacketsTests.cs:7-36`, `tests/Goose2Client.Tests/WindowResizeTests.cs:7-86` |
| Existing runtime tests are build-first and command-line gated, but `run_ui_scale.sh` currently `exec`s Godot without capturing output | `Scripts/GameManager.cs:218-221`, `Scripts/UiScaleSelfTest.cs:8-35`, `tools/tests/run_ui_scale.sh:1-28` |
| GodotSharp provides the required Tree, TreeItem, PopupMenu, ItemList, and clipboard APIs | `~/.nuget/packages/godotsharp/4.6.2/lib/net8.0/GodotSharp.xml:34940,35094,35154,120771,120885,121026,131911,187594,187666,187941,21137,254405-254412` |

The UI behavior remains aligned with `docs/plans/2026-09-26-gm-log-viewer-design.md:156-175`. Its earlier protocol sketch at lines 127-150 is superseded by revised server Part 2.

---

## Fixed client and wire contracts

### Exact metadata and response packets

Text metadata and LRX use canonical padded RFC 4648 Base64 over `UTF8Encoding(false, true)`. Page tokens use a separate raw canonical unpadded Base64Url grammar that decodes to exactly 16 bytes.

```text
LMT{windowId},{knownTypeId},{groupB64},{labelB64}
LMM{windowId},{knownMapId},{mapNameB64}
LMD{windowId},{defaultStartUnixMs},{defaultEndUnixMs}
LRB{windowId},{requestId}
LRD{windowId},{requestId},{rowOrdinal},{chunkIndex},{chunkCount},{base64Segment}
LRF{windowId},{requestId},{hasMore0or1},{currentPageTokenB64Url},{nextPageTokenB64Url}
LRX{windowId},{requestId},{safeMessageB64}
```

Field counts after the opcode are exactly 4, 3, 3, 2, 6, 5, and 3. Numeric parsing is invariant and non-throwing.

- Window/request IDs are positive `Int32`.
- Metadata type/map IDs use the full `Int32` wire type; LMD boundaries are signed `Int64` Unix milliseconds and must form a valid increasing UTC range.
- LRD `rowOrdinal` and `chunkIndex` are zero-based non-negative `Int32`; `chunkCount` is positive `Int32`; each segment is nonempty ASCII standard-Base64 alphabet/padding material and at most 12,288 characters.
- LRF accepts only `0` or `1`. Its current token is always a canonical nonempty 22-character Base64Url token. Its next token is canonical and nonempty exactly for `hasMore == 1`, otherwise empty. Tokens are raw fields, never passed through the text codec.
- LRX safe text uses the standard text codec.
- LMT/LMM are accepted only for the current window before LMD. Duplicate/conflicting/malformed current metadata is sticky through that window session, and valid LMD is the only metadata-complete marker.
- Packet handlers always return an immutable packet with `IsValid`, original ASCII wire length, and any recoverable positive window/request identity. This is required because `PacketManager` suppresses thrown parse failures before observers run.

The row is reconstructed only after all chunks for that row are concatenated in order. Decode standard Base64 once, require at most 262,144 decoded bytes, then parse one UTF-8 JSON value with this exact property order and shape:

```json
{
  "rowId": 0,
  "utcMilliseconds": 0,
  "typeId": 0,
  "typeIsInteger": true,
  "eventLabel": "",
  "eventGroup": "",
  "otherIdKind": "Unused",
  "primary": {
    "label": "",
    "kind": "Player",
    "id": 0,
    "name": "",
    "canQuickFilter": false
  },
  "related": null,
  "map": null,
  "raw": {
    "playerId": 0,
    "playerIdIsInteger": true,
    "otherId": 0,
    "otherIdIsInteger": true,
    "mapId": 0,
    "mapIdIsInteger": true,
    "mapX": 0,
    "mapXIsInteger": true,
    "mapY": 0,
    "mapYIsInteger": true
  },
  "summary": "",
  "originalText": ""
}
```

The client requires every property exactly once, in that order, with no extension properties or trailing JSON. `rowId`, `utcMilliseconds`, `typeId`, every non-null entity/map ID, and every raw numeric are signed JSON `Int64`. `typeIsInteger` and all five raw validity flags are retained. `primary` is required; `related` and `map` are independently nullable. Primary/related contain exactly `label`, `kind`, nullable `id`, `name`, and `canQuickFilter`; map contains exactly `id`, `name`, and `canQuickFilter`. `otherIdKind` remains separate from `related` and accepts the stable Part 1 names `Unused`, `Player`, `Guild`, `Item`, and `NpcTemplate`. Stable entity kinds are `Player`, `Item`, `Guild`, `NpcTemplate`, `Map`, and `StoredValue`; `StoredValue` must remain displayable and cannot be guessed into another kind. The parser preserves server strings and signed values without narrowing.

### Exact outbound Fresh and Page

```text
LQS{windowId},{requestId},F,{startMs},{endMs},{participantB64},{mapId},{typeIds},{textB64}
LQS{windowId},{requestId},P,{pageTokenB64Url}
```

- Fresh has exactly nine fields after `LQS`; Page has exactly four.
- The action is exactly uppercase `F` or `P`.
- Page sends no dates, participant, map, type, text, or other filter data.
- Window/request IDs are positive `Int32`; request IDs begin at 1, increment per successfully published request, and wrap from `Int32.MaxValue` to 1 without reusing the active ID.
- Fresh boundaries are signed `Int64` Unix milliseconds with `start < end`; map and type IDs are non-negative `Int32`.
- Participant is trimmed before standard Base64 encoding. Text remains literal and untrimmed. Their UTF-8 limits are 64 and 4,096 bytes.
- Selected type IDs are deduplicated and sorted numerically, then joined with `|`; empty means All, including unknown/future stored event values.
- Page token is one raw canonical 22-character Base64Url field.
- Complete LQS output is ASCII, at most 8,192 characters, and excludes the transport delimiter added by `NetworkClient.Send`.

`LogQuerySubmission` is a discriminated immutable model: Fresh contains only a filter snapshot; Page contains only a token and navigation intent. Mixed states are not representable.

### Filter, quick-action, and presentation decisions

- Presets are `Last hour`, `Previous 24 hours`, `Previous 7 days`, `Previous 30 days`, and `Custom UTC`. Initial LMD boundaries are retained exactly. Reapplying a preset captures the injected UTC instant once.
- Custom fields use invariant `yyyy-MM-dd HH:mm:ss`, parse as UTC, and reject invalid text, `start >= end`, spans over 31 days, and nonempty literal-text spans over 7 days.
- Participant is empty, an exact name, or `#` plus a positive `Int32`; names are not resolved client-side.
- Map is All, a selected current `Name (#ID)`, or raw `#positiveInt32`. An unselected partial name is a local error.
- Types use the six UI groups in this exact order: Communication, Sessions/Security, Social, Items/Economy, GM Actions, Other/Retired; entries within each group retain numeric LMT order. Empty selection means All. Selecting all current types remains explicit.
- Clear changes drafts only to a newly captured Previous-24-hours range plus empty participant/map/types/text. It does not send or clear committed rows.
- The table has exactly six unsorted columns: `UTC`, `Event type`, `Primary`, `Related`, `Map`, `Summary`, with at most 50 server-order rows.
- Table UTC is `yyyy-MM-dd HH:mm:ss.fff`; details/clipboard UTC is `yyyy-MM-ddTHH:mm:ss.fffZ`.
- Type quick-filter is available only when `typeIsInteger` is true and the row’s signed type value exactly matches a type present in the current LMT registry. Generic or out-of-domain values are display-only.
- Primary and related quick-filters are independent and require that exact projected entity to have `canQuickFilter == true` and a positive ID within `Int32`. Player-kind actions set participant to `#ID`; Map-kind actions set map to `#ID`; kinds without a corresponding client filter remain display-only.
- Projected map quick-filter independently requires `canQuickFilter == true` and a positive ID within `Int32`; it sets map to `#ID`.
- Quick actions mutate drafts only and never send.

Details and clipboard use LF separators and this exact 32-key order, replacing every prior shorter contract:

```text
Summary
UTC
Row ID
Type Label
Type ID
Type Is Integer
Group
Other ID Kind
Primary Label
Primary Kind
Primary ID
Primary Name
Primary Can Quick Filter
Related Label
Related Kind
Related ID
Related Name
Related Can Quick Filter
Map ID
Map Name
Map Can Quick Filter
Raw Player ID
Raw Player ID Is Integer
Raw Other ID
Raw Other ID Is Integer
Raw Map ID
Raw Map ID Is Integer
Raw Map X
Raw Map X Is Integer
Raw Map Y
Raw Map Y Is Integer
Original Text
```

Each output line is `Key: value`. Null related/map fields remain present with empty values. Booleans are lowercase `true`/`false`; signed numbers use invariant decimal. `Original Text: ` is followed verbatim by the stored text. The detail pane and clipboard both use this formatter, so semantic fields, every raw value, and every validity flag stay visible. Copy sends no packet.

### Search, chunk staging, and page-token history

- A Fresh request snapshots validated drafts. It leaves prior rows visible while loading.
- Every successful page has a nonempty current token. A Fresh LRF may issue any canonical current token and replaces history with `[currentToken]`; there is no empty first-page sentinel or inference.
- Previous and Next are Page requests. Previous submits `history[index - 1]`; Next submits the committed `nextToken`.
- A Page LRF is valid only when `currentToken` exactly equals the submitted Page token. On a successful forward Page, matching existing forward history is reused; differing forward history is truncated and the returned current token appended. Previous moves to the already stored token. History changes only at commit.
- Paging always uses the server-bound token only. It never resends filters, even when drafts were edited.
- LRB for the active identity creates one empty stage and includes its packet bytes in the 4,194,304-byte staged-response budget, counting one transport delimiter per packet.
- LRD requires LRB, contiguous row ordinals beginning at 0, all chunks for one row contiguously ordered from index 0, stable positive `chunkCount`, and completion of that row before the next ordinal. Duplicate, skipped, reversed, overlapping, or late chunks abort the whole stage.
- Completed rows are decoded/parsed only after concatenating all segments. The stage aborts on malformed segment material, malformed/noncanonical aggregate Base64, invalid UTF-8/JSON/shape/value, decoded row over 262,144 bytes, row 51, or staged bytes over 4,194,304.
- Matching LRF is included in the staged-byte budget and commits only when no row is incomplete, all ordering invariants hold, token semantics match the active Fresh/Page request, and row count is at most 50.
- Any matching malformed LRB/LRD/LRF, LRX, duplicate LRB, result before LRB, missing chunk discovered at LRF, or size violation aborts active staging and preserves the previous visible page/applied filters/history. A later LRF for that request cannot commit.
- Nonmatching window/request packets never mutate visible data. If result staging exists, any result identity mismatch discards and terminally aborts that stage; with no stage, stale traffic is ignored. Close, CLW, disconnect, socket error, replacement MKW, and scene teardown discard every partial row, accumulated Base64 segment, decoded row, and stage byte count.
- Search/Previous/Next are disabled while active; drafts and Clear remain editable. Search is disabled until valid LMD completes metadata.
- Inline states are `Waiting for log metadata…`, `Ready`, `Loading…`, `Showing N rows (up to 50), page P`, `No persisted logs matched.`, exact safe LRX text, local validation text, and protocol failure text. A persistent notice says `Recent entries may be delayed by up to ten minutes.`
- The applied-filter line describes only the committed Fresh filter snapshot. Dirty drafts show `Filters edited — Search to apply.` Paging never changes that snapshot.

### Publication and teardown boundaries

- `GameHud` creates one hidden persistent `LogViewerWindow` before gameplay packet dispatch.
- MKW frame 29 synchronously discards all prior metadata, request, chunk, rows, details, and token history before assigning/opening the replacement ID. LMT/LMM affect only that ID; valid LMD completes metadata; ENW reveals it.
- `NetworkClient.TryLogQuery(LogQuerySubmission, out string error)` is the sole production LQS boundary. It calls the exact formatter and invokes `Send` only on success.
- `LogViewerWindow` owns an internal injectable query-sender delegate with the same bool/out-error contract, defaulting to the current `NetworkClient.TryLogQuery` method. Unit and headless tests install a capturing sender; they never subclass or invoke nonvirtual `Send` and never require a socket.
- UI validation and active-state transition happen before invoking the sender. Sender rejection rolls back only the new active request and displays its safe local error.
- Packet callbacks run on the existing main-thread path, transition state once, then render one coherent snapshot.
- Client close captures a pure close request, hides/resets first, then calls `WindowButtonClick(Close, oldWindowId, 0)`. CLW/replacement/disconnect/socket error reset without WBC.
- `_ExitTree` removes every packet observer and both network-event delegates and clears staging.

---

## Task 1: Define all wire DTOs, strict codecs, exact packet parsers, row JSON parsing, and Fresh/Page formatting

**Files:**
- Create: `Scripts/Logs/LogWireModels.cs`
- Create: `Scripts/Network/ProtocolTextCodec.cs`
- Create: `Scripts/Network/LogPageTokenCodec.cs`
- Create: `Scripts/Network/Packets/LogPacketParsing.cs`
- Create: `Scripts/Network/Packets/LogTypeMetadataPacket.cs`
- Create: `Scripts/Network/Packets/LogMapMetadataPacket.cs`
- Create: `Scripts/Network/Packets/LogDefaultsMetadataPacket.cs`
- Create: `Scripts/Network/Packets/LogResultBeginPacket.cs`
- Create: `Scripts/Network/Packets/LogResultDataPacket.cs`
- Create: `Scripts/Network/Packets/LogResultFinishPacket.cs`
- Create: `Scripts/Network/Packets/LogResultErrorPacket.cs`
- Create: `Scripts/Network/Packets/LogRowJsonParser.cs`
- Create: `Scripts/Network/Packets/LogQueryPacket.cs`
- Create: `tests/Goose2Client.Tests/ProtocolTextCodecTests.cs`
- Create: `tests/Goose2Client.Tests/LogPageTokenCodecTests.cs`
- Create: `tests/Goose2Client.Tests/LogViewerPacketTests.cs`
- Create: `tests/Goose2Client.Tests/LogRowJsonParserTests.cs`
- Create: `tests/Goose2Client.Tests/LogQueryPacketTests.cs`

### Step 1: Write failing DTO/codec/token tests

`LogWireModels.cs` must be created in this task and contain every wire-level immutable DTO required by the parsers and formatter: metadata records, the immutable Fresh filter snapshot, Fresh/Page submissions, navigation intent, packet validity/identity data, LRD chunk data, LRF token data, row, projected entity, projected map, and raw persisted values. No formatter/parser may define a private substitute shape that Task 2 would need to translate.

`ProtocolTextCodecTests` pins empty/ASCII/delimiter/control/BMP/surrogate-pair round trips, canonical padded vectors, strict UTF-8, exact byte limits, and non-throwing rejection of whitespace, URL-safe characters, malformed length/padding/padding bits, and invalid UTF-8.

`LogPageTokenCodecTests` pins exact 22-character canonical unpadded Base64Url acceptance and rejects 21/23 characters, `+`, `/`, `=`, whitespace, non-ASCII, and alternate/noncanonical spellings. It never uses `ProtocolTextCodec`.

### Step 2: Write failing packet/JSON/query tests

`LogViewerPacketTests` directly invokes each handler with `PacketParser` and asserts:

1. Exact LMT/LMM/LMD/LRB/LRD/LRF/LRX field counts and one exact valid vector for each.
2. LRD carries only identity, ordinal, index/count, and one untouched segment; LRF carries raw current/next tokens.
3. LRF requires current token and enforces next-token iff `hasMore`; Fresh-vs-Page matching is not guessed in the packet parser.
4. Invariant numbers under a non-English process culture; positive identities; zero-based indexes; positive chunk count; segment maximum; signed LMD boundaries.
5. Wrong counts, overflow, signs/ranges, malformed text Base64, malformed token shape, non-ASCII segment data, and overlong segments produce `IsValid == false` without throwing.
6. Invalid packets preserve bounded recoverable identities and original wire length so active state can abort and enforce staged size.
7. `PacketManager` observers receive invalid log packet objects rather than losing them to its exception catch.

`LogRowJsonParserTests` pins:

1. The exact Part 2 property order, names, nesting, required explicit nulls, and no trailing/duplicate/unknown properties.
2. `Int64.MinValue`/`Int64.MaxValue` for row/type/entity/map/raw fields and pre-epoch/signed UTC milliseconds without narrowing.
3. Every validity flag, independent `otherIdKind` and related projection, nullable related/map, and `StoredValue` entity kind.
4. Unicode and escaped commas/pipes/`\x1`/NUL/CR/LF in every text-bearing location.
5. Exactly 262,144 decoded UTF-8 bytes accepted when valid JSON; one byte over, invalid UTF-8, malformed JSON, wrong JSON primitive types, invalid enum names, missing properties, and trailing JSON rejected without partial rows.

`LogQueryPacketTests` pins exact output strings for both variants:

1. Fresh has 9 fields and exact uppercase `F`; Page has 4 fields and exact uppercase `P`.
2. Page contains only IDs/action/raw 22-character token and no filter material.
3. Fresh uses trimmed participant, literal text, sorted/distinct types, empty All, standard Base64, invariant signed milliseconds, and no transport delimiter.
4. Limits are participant 64 bytes, text 4,096 bytes, selected type count no greater than current LMT count, and complete packet 8,192 ASCII characters.
5. Invalid/mixed submission states cannot be constructed; invalid IDs/ranges/map/types/tokens return a safe error and no packet.

### Step 3: Run red

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~ProtocolTextCodecTests|FullyQualifiedName~LogPageTokenCodecTests|FullyQualifiedName~LogViewerPacketTests|FullyQualifiedName~LogRowJsonParserTests|FullyQualifiedName~LogQueryPacketTests"
```

Expected: compile failures because the wire models and helpers do not exist.

### Step 4: Implement pure wire boundaries

- Implement all shared wire records first in `Scripts/Logs/LogWireModels.cs`; Task 2 imports them directly.
- Split `GetWholePacket()` after exact opcode verification and enforce exact count instead of using culture-sensitive parser conversions.
- Preserve invalid packet identity/length, but never preserve partially decoded semantic data as valid.
- Parse row JSON with `Utf8JsonReader` or equivalent explicit token walking. Do not deserialize permissively into reflection models.
- Keep standard text Base64, row-byte Base64, and raw Base64Url token validation as distinct APIs.
- Format one exact packet from the discriminated submission; do not read Godot controls or viewer state.

### Mutation impact

Pure models and transformations only. No listeners, controls, state transitions, socket sends, clipboard writes, or lifecycle mutations.

### Step 5: Green and commit

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~ProtocolTextCodecTests|FullyQualifiedName~LogPageTokenCodecTests|FullyQualifiedName~LogViewerPacketTests|FullyQualifiedName~LogRowJsonParserTests|FullyQualifiedName~LogQueryPacketTests"
dotnet build Goose2ClientGodot.csproj --no-restore
```

```bash
git add Scripts/Logs/LogWireModels.cs Scripts/Network/ProtocolTextCodec.cs Scripts/Network/LogPageTokenCodec.cs Scripts/Network/Packets/LogPacketParsing.cs Scripts/Network/Packets/LogTypeMetadataPacket.cs Scripts/Network/Packets/LogMapMetadataPacket.cs Scripts/Network/Packets/LogDefaultsMetadataPacket.cs Scripts/Network/Packets/LogResultBeginPacket.cs Scripts/Network/Packets/LogResultDataPacket.cs Scripts/Network/Packets/LogResultFinishPacket.cs Scripts/Network/Packets/LogResultErrorPacket.cs Scripts/Network/Packets/LogRowJsonParser.cs Scripts/Network/Packets/LogQueryPacket.cs tests/Goose2Client.Tests/ProtocolTextCodecTests.cs tests/Goose2Client.Tests/LogPageTokenCodecTests.cs tests/Goose2Client.Tests/LogViewerPacketTests.cs tests/Goose2Client.Tests/LogRowJsonParserTests.cs tests/Goose2Client.Tests/LogQueryPacketTests.cs
git commit -m "feat: define revised GM log viewer wire protocol"
```

### Invariant-to-test matrix

| Invariant | Test set |
|---|---|
| Fresh=9 and Page=4 with incompatible payloads | exact query vectors/count failures |
| Tokens stay canonical raw Base64Url | token codec and exact LRF/Page assertions |
| LRD has exactly six chunk fields | exact packet vectors/count failures |
| Signed `Int64`, validity flags, and independent semantic projections survive | JSON extremes/projection tests |
| JSON shape/order is deterministic and closed | explicit property/order/extra-field tests |
| Malformed input never disappears through a parser throw | invalid packet observer tests |

---

## Task 2: Implement pure filters, strict chunk staging, token-history paging, and complete details

**Dependency:** This task consumes `Scripts/Logs/LogWireModels.cs` and Task 1 parsers directly. It must not recreate packet, row, entity, map, raw-value, or submission DTOs.

**Files:**
- Create: `Scripts/Logs/LogViewerModels.cs`
- Create: `Scripts/Logs/LogFilterValidator.cs`
- Create: `Scripts/Logs/LogResponseAssembler.cs`
- Create: `Scripts/Logs/LogViewerState.cs`
- Create: `Scripts/Logs/LogDetailsFormatter.cs`
- Create: `tests/Goose2Client.Tests/LogFilterValidatorTests.cs`
- Create: `tests/Goose2Client.Tests/LogResponseAssemblerTests.cs`
- Create: `tests/Goose2Client.Tests/LogViewerStateTests.cs`
- Create: `tests/Goose2Client.Tests/LogDetailsFormatterTests.cs`

### Step 1: Write failing metadata/filter tests

`LogFilterValidatorTests` covers:

1. Ordered LMT/LMM collection, exact six group names, duplicate/conflicting IDs, sticky malformed metadata, and LMD as the only completion marker.
2. Exact initial LMD milliseconds and fixed-clock 1-hour/24-hour/7-day/30-day presets.
3. Exact UTC custom format and 31-day/7-day boundaries, including one millisecond over.
4. Participant empty/name/`#positiveInt32`, UTF-8 boundary, and rejection of zero/negative/overflow IDs.
5. Map All/current canonical selection/raw `#positiveInt32`, case-insensitive suggestions, and rejection of unselected partial text.
6. Empty type selection remains All; explicit current selections remain sorted/distinct and are never collapsed.
7. Literal text preserves leading/trailing spaces, `%`, `_`, backslash, commas, controls, and Unicode.
8. Clear mutates drafts only and captures one new UTC instant.

### Step 2: Write failing assembler and state tests

`LogResponseAssemblerTests` drives Task 1 packet DTOs and pins:

1. LRB starts one empty stage; zero rows plus LRF is valid.
2. Two rows with multiple chunks each, including aggregate Base64 quartet splits, reconstruct only after contiguous indexes and remain staged.
3. Chunks may arrive across arbitrary calls/ticks, but row ordinals are contiguous from zero and chunks for one row cannot be interleaved with another row.
4. Duplicate/skipped/reversed index, changed chunk count, skipped/repeated ordinal, LRD before/after stage, duplicate LRB, and LRF with an incomplete row abort irreversibly.
5. Malformed segment, aggregate Base64, UTF-8, JSON, property shape, enum, or numeric value aborts irreversibly.
6. Exactly 50 rows pass; row 51 aborts. Exactly 262,144 decoded bytes pass; one over aborts. Exactly 4,194,304 staged ASCII bytes including synthetic delimiters pass; one over aborts.
7. LRX, explicit abort, close, disconnect, replacement, and disposal clear all chunks/decoded rows/byte counts. A later LRF cannot recover an aborted stage.

`LogViewerStateTests` covers:

1. New/replacement open starts with request ID 1 and Search disabled until valid LMD.
2. Fresh snapshots drafts, has no token, stays loading without clearing committed rows, and accepts a valid issued current token at finish.
3. First successful Fresh history is exactly `[nonemptyCurrentToken]`; no empty value appears in state or submissions.
4. Previous and Next produce Page submissions with only their target token. Draft edits and committed filters are not serialized into Page.
5. A Page finish commits only when returned current token exactly equals the submitted token.
6. Forward history reuse/truncation and backward movement occur only on successful commit; failed paging leaves rows/index/history untouched.
7. Matching LRB/LRD data remains invisible until matching valid LRF; LRF atomically swaps rows, applied Fresh filters, tokens, status, and selection.
8. Matching malformed packet, assembler failure, LRX, duplicate begin, missing chunk, oversize, and finish after abort preserve the old page and display only a safe inline error.
9. Nonmatching result identity cannot mutate visible data and aborts any existing stage; without a stage it is ignored. MKW replacement, CLW, client close, disconnect, socket error, and teardown clear active/staged/visible/details/history/metadata/drafts.
10. Request IDs wrap safely; double submission while active is impossible.
11. Type quick-filter requires `typeIsInteger` and exact current LMT membership. Primary/related/map actions require their own server `canQuickFilter` plus positive `Int32` ID; `StoredValue`, false flags, null, zero, negative, and out-of-domain IDs are display-only.
12. Waiting/loading/success/empty/safe-LRX/protocol/dirty-applied status text is exact.

### Step 3: Write failing details/clipboard tests

`LogDetailsFormatterTests` pins:

1. Exact 32 keys and exact order above, LF-only separators, lowercase booleans, invariant signed numbers, and ISO UTC.
2. Every semantic field plus type validity and all raw values/validity flags, including `Int64` extremes and invalid raw integers.
3. Null related/map produce all corresponding empty lines; independent `otherIdKind` and projected `StoredValue` remain distinct.
4. Original text, summary, names, labels, Unicode, commas, pipes, `\x1`, NUL, CR, and LF are preserved without BBCode/CSV escaping or omission.
5. Table projection is exactly six columns and uses the server summary/labels without client reinterpretation.
6. Details and clipboard call the same formatter and do not mutate state.

### Step 4: Run red

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogFilterValidatorTests|FullyQualifiedName~LogResponseAssemblerTests|FullyQualifiedName~LogViewerStateTests|FullyQualifiedName~LogDetailsFormatterTests"
```

Expected: compile failures because the pure model/state components do not exist.

### Step 5: Implement state transitions

- `LogResponseAssembler` owns all chunk buffers and limits; `LogViewerState` owns identity, active submission, committed page, and token history.
- Count original ASCII packet length plus one delimiter for LRB/LRD/LRF. Never retain more than the 4 MiB bound.
- Decode a row only at its final chunk. Keep decoded rows private until finish.
- Mark an active response terminally aborted on the first matching protocol error and ignore its later result packets.
- Fresh finish accepts the server-issued current token and replaces applied filters/history. Page finish requires exact submitted-token equality and never changes applied filters.
- Keep drafts editable while active and compare them to applied filters for the dirty indicator.
- Details formatting reads the immutable wire row and performs no semantic reconstruction.

### Mutation impact

All mutation is in non-Godot in-memory state. Only successful LRF changes visible rows/history. Every lifecycle reset erases staged and committed sensitive values. No network, clipboard, settings, or scene mutation occurs.

### Step 6: Green and commit

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogFilterValidatorTests|FullyQualifiedName~LogResponseAssemblerTests|FullyQualifiedName~LogViewerStateTests|FullyQualifiedName~LogDetailsFormatterTests"
dotnet build Goose2ClientGodot.csproj --no-restore
```

```bash
git add Scripts/Logs/LogViewerModels.cs Scripts/Logs/LogFilterValidator.cs Scripts/Logs/LogResponseAssembler.cs Scripts/Logs/LogViewerState.cs Scripts/Logs/LogDetailsFormatter.cs tests/Goose2Client.Tests/LogFilterValidatorTests.cs tests/Goose2Client.Tests/LogResponseAssemblerTests.cs tests/Goose2Client.Tests/LogViewerStateTests.cs tests/Goose2Client.Tests/LogDetailsFormatterTests.cs
git commit -m "feat: stage GM log pages and token history"
```

### Invariant-to-test matrix

| Invariant | Test set |
|---|---|
| Partial/malformed/oversize responses never become visible | assembler failure and atomic-state tests |
| Multi-chunk rows preserve exact Base64/JSON bytes | quartet-split and text round-trip tests |
| Fresh starts history with an issued nonempty token | first-page history tests |
| Previous/Next are Page and returned current token must match | submission and mismatch tests |
| Quick actions obey current metadata/server eligibility/domain | action availability matrix |
| Details expose all semantic/raw/validity data | exact 32-key formatter tests |
| Every lifecycle boundary destroys chunks and rows | close/disconnect/replacement matrix |

---

## Task 3: Build the responsive scene and bind controls to pure state

**Files:**
- Create: `Scenes/UI/LogViewerWindow.tscn`
- Create: `Scripts/UI/LogViewerWindow.cs`
- Create: `Scripts/UI/LogViewerLayout.cs`
- Modify: `Scripts/UI/DefaultWindowLayout.cs:9-44`
- Create: `tests/Goose2Client.Tests/LogViewerLayoutTests.cs`
- Create: `tests/Goose2Client.Tests/LogViewerSceneTests.cs`
- Modify: `tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs:16-29`

### Step 1: Write failing layout and scene-contract tests

`LogViewerLayoutTests` pins 1000×620 design size, 620×340 minimum, margins, six Tree column minimums/expand ratios, suggestion-list maximum height, and results/details split at minimum/default/design/high-DPI canvases.

`LogViewerSceneTests` reads the `.tscn` and asserts:

1. Standard root/chrome paths, `WindowName = "LogViewer"`, full-rect background, title, close, and anchored content.
2. Freshness notice, preset/custom UTC controls, participant, grouped types, map field/suggestions, literal text, Search, and Clear.
3. Separate inline status and applied-filter labels.
4. One unsorted six-column Tree, read-only plain details, exact quick-action buttons, Copy, Previous, and Next.
5. Containers make table/details consume growth; controls remain reachable at minimum size.
6. No sort, total count, refresh timer, export, or save-search control.

Extend `DefaultWindowLayoutTests` so `LogViewer` is a centered dialog without changing existing classifications.

### Step 2: Run red

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogViewerLayoutTests|FullyQualifiedName~LogViewerSceneTests|FullyQualifiedName~DefaultWindowLayoutTests"
```

### Step 3: Author scene and presentation code

- Use standard `BaseWindow` chrome, hidden default visibility, resizing, and `LogViewerLayout.MinSize`.
- Use compact filter rows, capped map suggestions, separate freshness/status/applied labels, and an expanding horizontal results/details split.
- Build type popup actions separately from event IDs and keep checkable selections open.
- Store map IDs and committed row indexes in Godot metadata rather than parsing display strings.
- Mutate draft/selection state first, then render. Enter submits only through the same Search path.
- Populate the Tree only from committed rows. Display full signed values/fallback labels without narrowing.
- Render details with the exact Task 2 formatter; Copy passes that same string to `DisplayServer.ClipboardSet`.
- Show type/entity/map quick buttons only under Task 2 eligibility rules.
- In this task, the window owns controls/rendering but does not yet subscribe or send. Define the internal query-sender delegate/property shape now so Task 4 can inject production and capture implementations without changing UI handlers.

### Mutation impact

One hidden scene and one dialog classification are introduced, but the scene is not yet published by `GameHud`. Only placement/size use existing settings; metadata, filters, chunks, rows, details, and tokens are never persisted. Clipboard changes only on Copy.

### Step 4: Green and commit

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogViewerLayoutTests|FullyQualifiedName~LogViewerSceneTests|FullyQualifiedName~DefaultWindowLayoutTests|FullyQualifiedName~LogDetailsFormatterTests"
dotnet build Goose2ClientGodot.csproj --no-restore
```

```bash
git add Scenes/UI/LogViewerWindow.tscn Scripts/UI/LogViewerWindow.cs Scripts/UI/LogViewerLayout.cs Scripts/UI/DefaultWindowLayout.cs tests/Goose2Client.Tests/LogViewerLayoutTests.cs tests/Goose2Client.Tests/LogViewerSceneTests.cs tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs
git commit -m "feat: build the GM log viewer scene"
```

### Invariant-to-test matrix

| Invariant | Test set |
|---|---|
| All approved controls/actions exist and deferred features do not | scene contract |
| Six server-order columns remain usable through scale/resize | layout metrics and scene contract |
| Details/Copy share complete stable text | formatter and presenter contract |
| Quick actions are projections of pure eligibility | presenter visibility tests |
| Only placement/size can persist | dialog/settings contract |

---

## Task 4: Publish frame 29 and wire exact outbound seam, packets, close, replacement, and disconnect

**Files:**
- Modify: `Scripts/WindowFrames.cs:3-33`
- Modify: `Scripts/UI/GameHud.cs:12-30,77-104`
- Modify: `Scripts/UI/LogViewerWindow.cs`
- Modify: `Scripts/Network/NetworkClient.cs:87-108,277-280`
- Create: `tests/Goose2Client.Tests/LogViewerLifecycleTests.cs`
- Create: `tests/Goose2Client.Tests/LogViewerIntegrationContractTests.cs`

### Step 1: Write failing lifecycle and sender-seam tests

`LogViewerLifecycleTests` covers:

1. Existing frame values remain unchanged and `LogViewer == 29`.
2. Frame-29 MKW resets before assignment; other MKW frames are ignored; current LMT/LMM/LMD/ENW publish in order.
3. Search invokes the injected sender once with Fresh. Previous/Next invoke it once with Page and exact target token; no action double-submits while active.
4. A capturing sender receives exact Task 1 submissions without constructing a socket or calling `NetworkClient.Send`.
5. Sender rejection rolls back active state, leaves committed rows/history intact, and shows its safe error.
6. Formatter tests pin the exact string, while a source contract verifies `NetworkClient.TryLogQuery(LogQuerySubmission, out string error)` calls that formatter, calls `Send` only on success, and returns a safe format error without a socket-based test.
7. Packet routing preserves multi-call chunks and commits only LRF; malformed matching input aborts and blocks later finish.
8. Client close resets/hides before exactly one `WBC2,{windowId},0,0,0`; CLW/disconnect/socket error/replacement reset without WBC.
9. Closing, disconnecting, or replacing after partial chunks clears assembler memory and makes all late chunks/finish stale.

`LogViewerIntegrationContractTests` statically verifies:

1. `GameHud` exposes/instantiates exactly one `LogViewerWindow` before managers.
2. `_Ready`/`_ExitTree` symmetrically add/remove MKW, ENW, CLW, LMT, LMM, LMD, LRB, LRD, LRF, LRX, `Disconnected`, and `SocketError`.
3. Every explicit Search/Previous/Next path calls only the internal query sender; production initialization binds it to `NetworkClient.TryLogQuery`.
4. No test or window path subclasses `NetworkClient`, overrides `Send`, opens a socket, or calls `Send` directly for LQS.

### Step 2: Run red

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogViewerLifecycleTests|FullyQualifiedName~LogViewerIntegrationContractTests"
```

### Step 3: Wire lifecycle and publication

- Append `LogViewer = 29` without renumbering.
- Add one persistent HUD scene/reference before manager creation.
- Add the exact public API `NetworkClient.TryLogQuery(LogQuerySubmission submission, out string error)`. On formatter success it calls existing `Send` once and returns true; on failure it sends nothing and returns false.
- Bind `LogViewerWindow`’s internal sender delegate to `GameManager.Instance.NetworkClient.TryLogQuery`. Keep it replaceable by internal tests/self-test after scene creation.
- Register only after controls/state exist and remove symmetrically.
- Route packet validity plus recoverable identity and wire length to state. Matching malformed packets use the terminal protocol-abort path.
- Render once after each transition.
- On local close, capture/reset/hide/render before WBC. On server/lifecycle teardown, reset/hide without WBC.

### Mutation impact

The HUD now always owns one hidden viewer. Only Search/Previous/Next can publish LQS. Unit/headless query tests use the injected capture sender and cannot touch the real socket. Every close/replacement/disconnect path destroys complete and partial sensitive state.

### Step 4: Green and commit

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~LogViewerLifecycleTests|FullyQualifiedName~LogViewerIntegrationContractTests|FullyQualifiedName~LogViewerPacketTests|FullyQualifiedName~LogQueryPacketTests|FullyQualifiedName~LogResponseAssemblerTests|FullyQualifiedName~LogViewerStateTests"
dotnet build Goose2ClientGodot.csproj --no-restore
```

```bash
git add Scripts/WindowFrames.cs Scripts/UI/GameHud.cs Scripts/UI/LogViewerWindow.cs Scripts/Network/NetworkClient.cs tests/Goose2Client.Tests/LogViewerLifecycleTests.cs tests/Goose2Client.Tests/LogViewerIntegrationContractTests.cs
git commit -m "feat: integrate revised GM log viewer lifecycle"
```

### Invariant-to-test matrix

| Invariant | Test set |
|---|---|
| Frame 29 is append-only with one HUD viewer | enum/HUD contract |
| Exact injectable sender isolates tests from socket/Send | capture/rejection/static seam tests |
| Explicit actions are the only LQS publishers | lifecycle publication matrix |
| Matching malformed packets terminally abort staging | route/late-finish tests |
| Close/replacement/disconnect erase all chunks and rows | lifecycle partial-response matrix |
| Listener teardown is symmetric | integration source contract |

---

## Task 5: Add captured headless runtime coverage, run full verification, and perform manual acceptance

**Files:**
- Create: `Scripts/LogViewerSelfTest.cs`
- Modify: `Scripts/GameManager.cs:218-221`
- Create: `tools/tests/run_log_viewer.sh`
- Modify: `Scripts/UiScaleSelfTest.cs`

### Step 1: Add the failing headless self-test

Add `+selftest=log_viewer` beside existing gates. `LogViewerSelfTest` uses the real autoload, HUD, scene, packet manager, controls, process frames, and an internal capturing query sender. It must:

1. Create the HUD at 1280×720 with isolated settings and assert exactly one hidden frame-29 viewer with expected design/minimum geometry and node paths.
2. Install the capture sender before any action. It records immutable submissions and returns configurable success/error; no real `Send` or socket is used.
3. Dispatch MKW, multiple ordered LMT records, delimiter/Unicode LMM names, LMD, and ENW. Assert Search enables only after LMD and controls reflect exact defaults/metadata.
4. Enter Fresh filters and submit. Assert one captured exact Fresh DTO/packet shape with nine fields.
5. Dispatch LRB and a two-row response whose Base64 is split across multiple LRD chunks and multiple process frames. Interleave unrelated non-result packets between chunks without violating active-row chunk order. Assert the Tree remains unchanged until LRF.
6. Dispatch valid Fresh LRF and assert two six-column rows, complete 32-key details, quick-action visibility rules, nonempty first history token, and paging state.
7. Click Next, assert one captured four-field Page with no filters, deliver chunks over multiple frames, and require returned current token to match. Click Previous and assert Page uses the stored first token.
8. Start separate responses that exercise duplicate/out-of-order/missing/oversize/malformed chunk and malformed JSON paths; after each, dispatch LRF and assert no commit and no stale partial buffers.
9. Dispatch LRX through an active request and assert exact inline text with no chat mutation.
10. Edit drafts after commit and assert applied text remains unchanged while dirty status appears.
11. Resize and apply 1×→2×→1× UI scale; assert content/split/tree/suggestions/buttons remain usable and geometry restores. Extend the global UI-scale audit to include the hidden viewer.
12. Begin a partial response, then separately exercise CLW and replacement MKW; assert hidden/reset state and no late finish commit. Unit lifecycle coverage supplies disconnect/socket-error paths that cannot safely tear down the runtime harness connection.
13. Print `[log_viewer_selftest] PASS` only after every assertion and request-capture check succeeds.

`tools/tests/run_log_viewer.sh` follows the build-first setup but must actually capture Godot stdout/stderr to a temporary file, preserve the Godot exit status, print the captured output, and fail unless both exit status is zero and `grep -F "[log_viewer_selftest] PASS"` succeeds. Do not use `exec` or a pipeline whose status can hide the Godot process. The new script contains only the shebang and commands, with no comments.

### Step 2: Run runtime red, then green

```bash
tools/tests/run_log_viewer.sh
```

Expected red before the gate/self-test/capture runner is complete; expected green only with a zero Godot exit and the PASS marker in captured output.

### Step 3: Run focused verification

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~ProtocolTextCodecTests|FullyQualifiedName~LogPageTokenCodecTests|FullyQualifiedName~LogViewerPacketTests|FullyQualifiedName~LogRowJsonParserTests|FullyQualifiedName~LogQueryPacketTests|FullyQualifiedName~LogFilterValidatorTests|FullyQualifiedName~LogResponseAssemblerTests|FullyQualifiedName~LogViewerStateTests|FullyQualifiedName~LogDetailsFormatterTests|FullyQualifiedName~LogViewerLayoutTests|FullyQualifiedName~LogViewerSceneTests|FullyQualifiedName~LogViewerLifecycleTests|FullyQualifiedName~LogViewerIntegrationContractTests|FullyQualifiedName~DefaultWindowLayoutTests"
dotnet build Goose2ClientGodot.csproj --no-restore
tools/tests/run_log_viewer.sh
tools/tests/run_ui_scale.sh
```

### Step 4: Run full client/worktree verification

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore
dotnet build Goose2ClientGodot.sln --no-restore
dotnet test Goose2ClientGodot.sln --no-build --no-restore
tools/tests/run_character_icon.sh
tools/tests/run_ui_scale.sh
tools/tests/run_log_viewer.sh
git diff --check
```

A C#-capable Godot binary is required; use the existing `GODOT_BIN` override rather than skipping runtime acceptance.

### Step 5: Manual checks against revised Parts 1/2

- [ ] Without `ViewLogs`, `/logs` opens nothing. With access, frame 29 opens one centered viewer with ordered metadata and the ten-minute notice.
- [ ] Reopening `/logs` replaces the viewer and erases metadata, complete rows, details, tokens, and any partial chunks.
- [ ] Verify all presets/custom boundaries, participant forms, current/raw map IDs, grouped type selection, and literal text.
- [ ] Capture outbound traffic: Fresh is exactly nine fields with `F`; Previous/Next are exactly four fields with `P` and one raw 22-character Base64Url token; Page carries no filters.
- [ ] Confirm editing/Clear/quick actions do not send. Enter submits one Fresh only when valid.
- [ ] Confirm loading leaves committed rows visible and disables only Search/Previous/Next.
- [ ] Inspect a 50-row page with signed out-of-`Int32` type/raw/entity/map/coordinate values, false validity flags, independent `otherIdKind`/related, null projections, and `StoredValue`.
- [ ] Copy rows containing tell/IP-like text, Unicode, commas, line breaks, controls, and invalid raw numerics; compare exact 32-key LF text and confirm no packet is sent.
- [ ] Confirm unknown type cannot become a draft filter unless represented by current LMT; confirm entity/map quick actions require server eligibility and positive `Int32` ID.
- [ ] Page Next through three pages and Previous twice. Each returned current token must match the Page request; returning to page one uses its original nonempty token. Draft edits remain unapplied during paging.
- [ ] Observe a large response spanning ticks: no rows appear before LRF. Close/revoke/disconnect/replacement after partial chunks must never expose or retain them.
- [ ] Exercise empty, busy, invalid-token, validation, oversize, and generic failure messages inline with no chat output.
- [ ] Resize all edges/corners and inspect minimum/default/high-DPI layouts; only placement/size survive relog.

### Mutation impact

Normal launches only gain the dormant self-test gate and UI-scale coverage. Runtime tests use a capture sender and isolated settings, so they cannot emit LQS. The runner creates only the same minimal generated-asset placeholders as existing scripts and always validates captured output.

### Step 6: Commit

```bash
git add Scripts/LogViewerSelfTest.cs Scripts/GameManager.cs tools/tests/run_log_viewer.sh Scripts/UiScaleSelfTest.cs
git commit -m "test: cover revised GM log viewer runtime"
```

### Invariant-to-test matrix

| Invariant | Test/check |
|---|---|
| Runtime sends are captured without socket dependence | capture-sender assertions |
| Chunks can span ticks/interleaving but commit only at LRF | multi-frame staged runtime response |
| Fresh/Page shapes and first-page current token are exact | captured submission and finish assertions |
| Malformed/oversize responses cannot be revived by LRF | runtime abort matrix |
| Real scene remains usable through resize/UI scale | geometry assertions and global audit |
| Runner cannot pass on stale/no-op execution | captured zero status plus PASS-marker requirement |

---

## Final API and protocol verification gate

1. Build both consumers of `Scripts/**/*.cs`: net10 xUnit and net8 Godot. Resolve API mismatches from the installed GodotSharp XML; do not guess method names.
2. Diff metadata packets against revised Part 2 lines 78-88; Fresh/Page against lines 90-106; token rules against lines 108-113; LRB/LRD/LRF/LRX and JSON against lines 138-200; lifecycle staging against lines 220-238.
3. Confirm counts are LMT 4, LMM 3, LMD 3, LRB 2, LRD 6, LRF 5, LRX 3, Fresh 9, and Page 4 after opcode removal.
4. Confirm tokens are raw canonical 22-character Base64Url in Page/LRF and never enter the standard text codec.
5. Confirm every row numeric remains signed `Int64`, every validity flag survives, `otherIdKind` and related remain separate, and `StoredValue` compiles/renders.
6. Confirm row and response bounds are 262,144 decoded bytes and 4,194,304 staged ASCII bytes including delimiters; row count is 50.
7. Confirm every `GetNode` path matches the scene and all Tree/PopupMenu/ItemList/clipboard calls match verified APIs.
8. Confirm all observer additions have removals and every close/disconnect/replacement path clears assembler buffers.
9. Search the implementation diff for newly added `//`, `///`, and XML docs and remove them unless the repository’s narrow exception applies.
10. Search changed plan/code/tests for obsolete packet shapes, filter-bearing Page requests, empty first-page tokens, narrowed row fields, permissive row JSON, or any shorter clipboard contract.
11. Run `git diff --check` and confirm no file outside this worktree changed.

---

## Final design alignment and red-team review

### Design alignment checklist

- **Part boundary:** Client consumes server metadata, deterministic row projection, summaries, and capabilities without reproducing SQL, authorization, descriptor meaning, or audits.
- **Actions:** Fresh and Page are explicit incompatible DTOs and exact 9/4-field packets; Page has no filters.
- **Tokens:** Every successful page has a raw canonical current token; first-page history starts nonempty; Page finish must echo the submitted token.
- **Rows:** Ordered chunks reconstruct one exact closed JSON shape with signed `Int64`, validity flags, independent semantic projections, and `StoredValue`.
- **Bounds:** Segment, row, response, and 50-row limits fail closed before commit.
- **Atomicity:** Only matching valid LRF commits. LRX, malformed input, missing/duplicate/out-of-order chunks, close, disconnect, and replacement destroy staging.
- **Filters:** Draft and applied Fresh filters remain distinct; Page relies only on server-bound token.
- **Quick actions:** Type requires current metadata; projected entity/map requires server eligibility and positive `Int32` domain.
- **Details/privacy:** One stable 32-key formatter exposes semantic fields plus all raw values/flags and is erased on lifecycle reset.
- **Testing:** Exact injectable sender and captured runtime output eliminate socket and false-pass dependencies.
- **Layout/features:** Dedicated resizable window follows existing chrome/scale behavior and adds no deferred feature.

### Red-team cases that must be green before release

| Attack/regression | Required client defense |
|---|---|
| Fresh omits `F`, Page carries filters, or action/count changes | Exact discriminated formatter and 9/4 count tests reject |
| Token has padding, standard alphabet, whitespace, wrong length, or is nested as text Base64 | Canonical raw Base64Url validation rejects |
| Fresh LRF issues a nonempty first token | Accept and start history with that exact token |
| Page LRF returns a different current token | Abort; old page/history remain |
| LRD chunks duplicate, skip, reverse, change count, or switch row early | Terminal assembler abort; later LRF cannot commit |
| Chunks split a Base64 quartet or span server ticks | Concatenate exact contiguous segments, then decode once |
| One row exceeds 256 KiB, response exceeds 4 MiB, or row 51 arrives | Terminal abort with no partial replacement |
| JSON omits/reorders/duplicates/adds a property or changes primitive type | Explicit parser rejects closed deterministic shape |
| Unknown/out-of-domain numbers arrive | Preserve signed `Int64`; never narrow or guess semantics |
| `otherIdKind` implies one thing while related projects another | Display both independently |
| Entity kind is `StoredValue` | Parse/display; no guessed quick action |
| Unknown type is clicked for filtering | No action unless exact current LMT/type-validity match |
| Server sets quick eligibility false or ID is out of positive `Int32` range | Entity/map action hidden/disabled |
| Filters are edited, then Next is clicked | Page sends token only; dirty drafts remain unapplied |
| Close/disconnect/replacement occurs with accumulated chunks | All chunk strings, decoded rows, byte counts, and tokens are erased |
| Malformed packet parser throws before state sees identity | Non-throwing invalid packet reaches abort path |
| Unit/headless test triggers real nonvirtual `Send` | Capture sender intercepts every query publication |
| Godot exits without running assertions | Shell runner requires zero status and captured PASS marker |
| Details contain markup/newlines/control text | Plain read-only details and exact formatter preserve text without interpretation |
| Copy or quick action sends traffic | Publication tests observe LQS only for Search/Previous/Next |
