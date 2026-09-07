# Map Editor Google Sheets Integration — Part 2: Connectivity Implementation Plan

**Goal:** Add application-wide Google authorization and spreadsheet selection, a tested Google Sheets transport, and provider-neutral Pull/Push orchestration with bounded retries, conflict detection, and ambiguous-write protection.

**Architecture:** Keep `MapEditor.GameData` independent of Google and Avalonia. It owns gateway contracts, sync-session state, retry policy, and Pull/Push coordination. Add `MapEditor.GameData.Google` for OAuth and the official Google Sheets client. `MapEditor.App` only persists the remembered URL and composes these services for Part 3; this part adds no menus, commands, view models, dialog methods, or Avalonia controls.

**Tech Stack:** C#/.NET 10 application and tests; Part 1 `MapEditor.GameData` contracts; `Google.Apis.Sheets.v4` 1.75.0.4178; `Google.Apis.Auth` 1.76.0; xUnit 2.9.2.

---

> Implement with @executing-plans, one task and commit at a time. Part 1 must be merged first. Do not add comments or doc strings unless an external-system constraint cannot be expressed in code.

## Scope

Included:

- installed-application OAuth with a loopback receiver and Sheets scope;
- standard per-user token storage and local token deletion on Disconnect;
- strict spreadsheet URL parsing, canonicalization, and remembering;
- provider-neutral gateway and sync contracts;
- Google reads and atomic replacement batches;
- Pull publication only after complete validation;
- Push validation, remote-conflict detection, Overwrite/Pull Instead/Cancel inputs, bounded retries, and ambiguous-write lockout;
- lazy application composition and an operator setup guide.

Excluded until Part 3:

- all Avalonia UI, menu/toolbar commands, progress display, conflict/dirty dialogs, and attaching sync sessions to `MapDocumentViewModel`/`WorkspaceViewModel`;
- close/disconnect prompting and map-row selection UI;
- editing, resize, rendering, clipboard, and undo integration beyond the Part 1 domain objects.

## Repository facts and Google API surface verified

- `AppSettings` is a positional record containing asset directory and theme; `AppSettingsStore.Update` rewrites one field while preserving the others: `src/MapEditor.App/Settings/AppSettings.cs` and `AppSettingsStore.cs`.
- Settings use camel-case JSON, tolerate missing fields, and are stored below platform-specific private configuration roots selected by `SettingsPathResolver`: `src/MapEditor.App/Settings/SettingsPathResolver.cs`.
- `WorkspaceViewModel` creates and owns map tabs and currently knows only map files; do not inject connectivity into it in Part 2: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`.
- `IEditorDialogs`, `AvaloniaEditorDialogs`, `EditorDialogsProxy`, and `FakeEditorDialogs` contain only existing map/asset interactions. They remain unchanged because connectivity UI is Part 3.
- `App.ComposeMainWindow` is the composition root and `ComposedEditor` exposes composed services to startup tests: `src/MapEditor.App/App.axaml.cs` and `tests/MapEditor.App.Tests/AppStartupTests.cs`.
- Part 1 fixes one-based worksheet row numbers (including header row 1), duplicate-preserving snapshots, schema/header validation, target-map replacement plans, and `SheetEditSession.MarkPushed()`: `docs/plans/2026-09-06-map-editor-google-sheets-part1-schema-foundation.md`.
- A throwaway `/tmp` `net10.0` project restored `Google.Apis.Sheets.v4` 1.75.0.4178 and `Google.Apis.Auth` 1.76.0. The package XML confirms:
  - `GoogleWebAuthorizationBroker.AuthorizeAsync(ClientSecrets, IEnumerable<string>, string, CancellationToken, IDataStore, ICodeReceiver)` and `LocalServerCodeReceiver` implement installed-app authorization;
  - `GoogleClientSecrets.FromStream(Stream)` loads desktop client JSON;
  - `FileDataStore(string, bool)`, `GetAsync<T>`, `DeleteAsync<T>`, and `ClearAsync` provide token persistence;
  - `SheetsService.Scope.Spreadsheets` is the read/write scope;
  - `SheetsService(BaseClientService.Initializer)` accepts `HttpClientInitializer`, `HttpClientFactory`, `ApplicationName`, and `BaseUri`;
  - `Spreadsheets.Values.BatchGet(string)` exposes `Ranges`, `MajorDimension`, and `ValueRenderOption`;
  - `Spreadsheets.Get(string)` exposes `Ranges` and `IncludeGridData`, and `SheetProperties.SheetId` supplies numeric sheet IDs;
  - `Spreadsheets.BatchUpdate(BatchUpdateSpreadsheetRequest, string)` accepts `Request.DeleteDimension` and `Request.AppendCells`; `DimensionRange` uses zero-based start-inclusive/end-exclusive indexes; `AppendCellsRequest` accepts `SheetId`, `Rows`, and `Fields`;
  - `BaseClientService.Initializer.DefaultExponentialBackOffPolicy` defaults to retrying HTTP 503. Set it to `ExponentialBackOffPolicy.None`; coordinator policy must own retries so writes are never invisibly retried.

## Locked Part 2 contracts

Keep Google types out of `MapEditor.GameData` public signatures. Exact result record names may be internal where noted, but preserve these responsibilities and state transitions:

```csharp
public readonly record struct SpreadsheetReference(string Id, string CanonicalUrl);

public static class SpreadsheetReferenceParser
{
    public static bool TryParse(string? value, out SpreadsheetReference reference);
    public static SpreadsheetReference Parse(string value);
}

public interface IGameDataGateway
{
    Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken);
    Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken);
    Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken);
    Task ReplaceOwnedRowsAsync(
        string spreadsheetId,
        ReplacementPlan spawnPlan,
        ReplacementPlan warpPlan,
        CancellationToken cancellationToken);
}

public sealed record RemoteGameData(
    IReadOnlyList<MapReference> Maps,
    IReadOnlyDictionary<int, NpcAppearance> Npcs,
    IReadOnlyList<RemoteRow<NpcSpawnRow>> Spawns,
    IReadOnlyList<RemoteRow<WarpRow>> Warps);

public sealed record RemoteOwnedRows(
    IReadOnlyList<RemoteRow<NpcSpawnRow>> Spawns,
    IReadOnlyList<RemoteRow<WarpRow>> Warps);

public enum GatewayFailureKind
{
    Authentication,
    Permission,
    NotFound,
    Schema,
    RateLimited,
    Temporary,
    Transport
}

public sealed class GameDataGatewayException : Exception
{
    public GatewayFailureKind Kind { get; }
    public string SpreadsheetId { get; }
    public bool RequestWasRejected { get; }
}

public sealed class GameDataSyncSession
{
    public string SpreadsheetId { get; }
    public int MapId { get; }
    public SheetEditSession Edits { get; }
    public SpawnSnapshot PulledSpawns { get; }
    public WarpSnapshot PulledWarps { get; }
    public bool RequiresPull { get; }
}

public enum PushConflictChoice
{
    Overwrite,
    PullInstead,
    Cancel
}
```

`ReadGameDataAsync` reads and validates all four required worksheets before returning. `ReadOwnedRowsAsync` is the Push preflight: it may return only spawn and warp rows, but it must also read and validate the current headers for `Maps`, `NPCs`, `NPC Spawns`, and `Warptiles` before returning. This makes schema mismatch block Push as well as Pull. The coordinator filters target-map rows defensively even though the gateway may optimize ranges later. `GameDataSyncSession` is created only from a successful Pull. Its snapshot/session replacement methods can be internal so only the coordinator can publish remote state, mark pushed, or clear `RequiresPull`.

Coordinator results must be explicit data, not dialogs or callbacks: map catalog; successful Pull; dirty-local rejection; validation rejection; conflict containing the latest remote owned rows; cancelled; pull-instead requested; pushed; and ambiguous outcome. Part 3 translates these results into Avalonia dialogs. An Overwrite call consumes the conflict result's latest rows; it does not silently perform another conflict prompt. Document the accepted compare/write race from the approved design.

## Retry and outcome rules

- Retry reads for `RateLimited`, `Temporary`, and `Transport` failures at most three total attempts with injected delays of 250 ms then 1 s; honor cancellation before each attempt and delay.
- Do not retry authentication, permission, not-found, schema, parse, mapping, or validation failures.
- For `ReplaceOwnedRowsAsync`, retry only an explicit rate-limit response whose exception has `RequestWasRejected == true`. Do not retry HTTP 5xx, timeout, cancellation after dispatch, connection reset, malformed success response, or any other missing confirmation.
- If a write was invoked and no confirmed success or confirmed rejection is available, set `RequiresPull`, keep `SheetEditSession.IsDirty`, and return ambiguous outcome. Every later Push is blocked until a complete Pull succeeds.
- A user cancellation observed before write dispatch is Cancelled, not ambiguous. Cancellation/transport failure after dispatch is ambiguous.
- Pull and conflict reads build temporary immutable values and publish nothing until every range, header, and row validates.

## Mutation impact matrix

| Mutation | Source of truth | Readers / derived state | Required propagation | Failure atomicity |
|---|---|---|---|---|
| Remember spreadsheet | `AppSettings.SpreadsheetUrl` | Part 3 spreadsheet field and composition consumers | parse → canonical URL → `AppSettingsStore.Update` | Invalid input never changes settings; save failure leaves prior file intact |
| Connect | OAuth token in `FileDataStore` | `UserCredential`, `SheetsService` factory | client JSON → browser/loopback → token store → connection state | Failed/cancelled auth publishes no connected client |
| Disconnect | OAuth token key and in-memory credential | future API calls | clear credential reference → `DeleteAsync<TokenResponse>(userKey)` | Local deletion failure is surfaced; no false disconnected-success result |
| Pull | remote four-sheet read | snapshots, `SheetEditSession`, map/NPC references | complete reads → header/map validation → map rows → new sync session | Existing session remains byte-for-byte observable on any failure |
| Push conflict check | latest owned rows | conflict result or replacement plans | retryable read → multiset compare → explicit decision | No write occurs before validation and decision |
| Push batch | Google spreadsheet rows for one map | pulled snapshots and dirty baseline | plans → one `BatchUpdateSpreadsheetRequest` → confirmed response → promote baseline | Other maps are untouched; uncertain response locks session to Pull |

## Invariant matrix

| Invariant | Required proof |
|---|---|
| Parser cannot select a lookalike/non-Google URL | host/scheme/userinfo/path adversarial parser tests |
| Remembered URL contains no token and remains independent of OAuth | settings round-trip and disconnect tests |
| OAuth tokens live outside settings and Disconnect deletes the exact token key | real temporary `FileDataStore` test |
| Provider-neutral domain references no Google or Avalonia assembly | project references plus architecture test/build inspection |
| Pull never partially replaces local state | late fourth-range/header/mapping failure coordinator tests |
| Duplicate rows and order-independent comparison retain Part 1 semantics | coordinator conflict tests with reordered duplicates and changed multiplicity |
| Other maps are never deleted | Google request-body assertions and Part 1 planner tests |
| Deletes use zero-based descending Google row ranges | batch builder unit test and loopback transport test |
| One Push uses one atomic Sheets spreadsheet batch | loopback server observes exactly one batch-update POST |
| No hidden 503 retry can duplicate a write | service initializer test and single-request ambiguous-503 test |
| Ambiguous Push cannot be repeated before Pull | state-transition test: transport failure → blocked Push → successful Pull → enabled Push |
| Part 2 has no UI behavior | no changes to dialogs, workspace, window XAML, or view models |

## Task 1: Parse and remember spreadsheet URLs

**Files:**
- Create: `src/MapEditor.GameData/Connectivity/SpreadsheetReferenceParser.cs`
- Create: `tests/MapEditor.GameData.Tests/Connectivity/SpreadsheetReferenceParserTests.cs`
- Modify: `src/MapEditor.App/Settings/AppSettings.cs`
- Modify: `src/MapEditor.App/Settings/AppSettingsStore.cs`
- Modify: `tests/MapEditor.App.Tests/SettingsTests.cs`

**Mutation impact:** Adds one optional settings field. Existing settings without it must deserialize identically; asset/theme updates must preserve it. Persist only a canonical URL, never pasted query parameters, fragments, credentials, or OAuth data.

**Step 1: Write failing tests**

Test exact `https://docs.google.com/spreadsheets/d/{id}` URLs with `/edit`, trailing slash, query, and fragment; canonical output; surrounding whitespace; and IDs containing letters, digits, `_`, and `-`. Reject null/blank, bare IDs, HTTP, userinfo, ports, subdomain/lookalike hosts, extra/missing path segments, percent-encoded separators, and IDs containing punctuation. Add settings tests for absent-field compatibility, canonical URL round trip, and `Update` preserving URL/asset/theme independently.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SpreadsheetReferenceParser'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~Settings'
```

Expected: FAIL because parser and settings member are absent.

**Step 3: Implement minimum**

Use `Uri.TryCreate` plus ordinal host/path validation, not a substring regex. Require HTTPS, default port, no userinfo, exact `docs.google.com`, and path segments `spreadsheets/d/{id}` followed only by empty or `edit`. Decode no path component before structural validation. Return `https://docs.google.com/spreadsheets/d/{id}`. Add `string? SpreadsheetUrl = null` after existing positional defaults and project it through the private settings DTO.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SpreadsheetReferenceParser'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~Settings'
```

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Connectivity tests/MapEditor.GameData.Tests/Connectivity \
  src/MapEditor.App/Settings/AppSettings.cs src/MapEditor.App/Settings/AppSettingsStore.cs \
  tests/MapEditor.App.Tests/SettingsTests.cs
git commit -m "feat: remember Google spreadsheet selection"
```

## Task 2: Add provider-neutral gateway and sync state contracts

**Files:**
- Create: `src/MapEditor.GameData/Connectivity/IGameDataGateway.cs`
- Create: `src/MapEditor.GameData/Connectivity/GameDataGatewayException.cs`
- Create: `src/MapEditor.GameData/Sync/GameDataSyncSession.cs`
- Create: `src/MapEditor.GameData/Sync/SyncResults.cs`
- Create: `tests/MapEditor.GameData.Tests/Sync/GameDataSyncSessionTests.cs`
- Create: `tests/MapEditor.GameData.Tests/Architecture/DependencyBoundaryTests.cs`

**Mutation impact:** A sync session becomes the sole mutable synchronization state: confirmed spreadsheet/map IDs, immutable pull snapshots, editable rows, references, and ambiguous-write latch. Gateway DTO constructors defensively copy collections so fake or provider collections cannot drift after publication.

**Step 1: Write failing tests**

Test construction from pulled data, target-map filtering, duplicate retention, immutable/copy-owned maps/NPCs/remote rows, internal successful-push promotion, Pull replacement, and ambiguous latch behavior. Add an architecture test reading referenced assembly names from `MapEditor.GameData.dll` and asserting no `Google.*` or `Avalonia*` reference.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataSyncSession|FullyQualifiedName~DependencyBoundary'
```

Expected: FAIL because contracts and session do not exist.

**Step 3: Implement minimum**

Add the locked contracts and explicit coordinator result records. Construct a fresh `SheetEditSession` from pulled map-owned values and matching snapshots. Keep mutation methods internal. Reject nonmatching row map IDs at publication rather than allowing a session to own another map's rows. Do not reference `MapDocument`, view models, dialogs, Google exceptions, or Avalonia.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataSyncSession|FullyQualifiedName~DependencyBoundary'
dotnet build src/MapEditor.GameData/MapEditor.GameData.csproj
```

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Connectivity src/MapEditor.GameData/Sync \
  tests/MapEditor.GameData.Tests/Sync tests/MapEditor.GameData.Tests/Architecture
git commit -m "feat: define game data synchronization boundary"
```

## Task 3: Add installed-app OAuth and token lifecycle

**Files:**
- Create: `src/MapEditor.GameData.Google/MapEditor.GameData.Google.csproj`
- Create: `src/MapEditor.GameData.Google/Auth/GoogleConnection.cs`
- Create: `src/MapEditor.GameData.Google/Auth/GoogleOAuthClientConfig.cs`
- Create: `src/MapEditor.GameData.Google/Auth/GoogleTokenPathResolver.cs`
- Create: `tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj`
- Create: `tests/MapEditor.GameData.Google.Tests/Auth/GoogleConnectionTests.cs`
- Create: `tests/MapEditor.GameData.Google.Tests/Auth/GoogleTokenStoreTests.cs`
- Modify: `Goose2ClientGodot.sln`

**Mutation impact:** OAuth writes `TokenResponse` through a full-path `FileDataStore` below the same platform application-data root as settings, in a sibling `google-tokens` directory. It never writes secrets/tokens to `settings.json`. In-memory credentials are published only after authorization completes.

**Step 1: Scaffold and write failing tests**

Add package references at the verified versions and project references to `MapEditor.GameData`. Add the projects under the solution `src`/`tests` folders. Behind an internal broker delegate, test exact scope, stable user key (`map-editor-user`), supplied `IDataStore`, `LocalServerCodeReceiver` with loopback-IP strategy, cancellation, failed authorization, idempotent Connect, and Disconnect. In a temporary directory, use the real `FileDataStore(path, true)` to store/get/delete a `TokenResponse` and prove Disconnect deletes only the stable key.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj --filter 'FullyQualifiedName~GoogleConnection|FullyQualifiedName~GoogleTokenStore'
```

Expected: FAIL because Google connectivity types are absent.

**Step 3: Implement minimum**

Load desktop client JSON lazily with `GoogleClientSecrets.FromStream`; reject missing/invalid/incorrect client type with a typed configuration exception that names the path but never file contents. Call the verified `GoogleWebAuthorizationBroker.AuthorizeAsync` overload with `SheetsService.Scope.Spreadsheets`, the stable user key, full-path `FileDataStore`, and `LocalServerCodeReceiver(...ForceLoopbackIp)`. Disconnect clears the in-memory credential and calls `DeleteAsync<TokenResponse>(userKey)`; remote revocation is documented as a separate Google Account action, not silently conflated with local disconnect.

Resolve client JSON in this order: `GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT` absolute path, then `google-oauth-client.json` beside the executable. Resolve tokens beside the settings directory using the existing platform root conventions. Do not commit client JSON.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj --filter 'FullyQualifiedName~GoogleConnection|FullyQualifiedName~GoogleTokenStore'
dotnet build src/MapEditor.GameData.Google/MapEditor.GameData.Google.csproj
```

**Step 5: Commit**

```bash
git add Goose2ClientGodot.sln src/MapEditor.GameData.Google tests/MapEditor.GameData.Google.Tests
git commit -m "feat: add Google OAuth token lifecycle"
```

## Task 4: Implement the Google Sheets gateway and real loopback transport test

**Files:**
- Create: `src/MapEditor.GameData.Google/Sheets/GoogleSheetsGateway.cs`
- Create: `src/MapEditor.GameData.Google/Sheets/GoogleSheetsServiceFactory.cs`
- Create: `src/MapEditor.GameData.Google/Sheets/GoogleBatchBuilder.cs`
- Create: `src/MapEditor.GameData.Google/Sheets/GoogleFailureMapper.cs`
- Create: `tests/MapEditor.GameData.Google.Tests/Sheets/GoogleBatchBuilderTests.cs`
- Create: `tests/MapEditor.GameData.Google.Tests/Sheets/GoogleSheetsGatewayTests.cs`
- Create: `tests/MapEditor.GameData.Google.Tests/Sheets/GoogleSheetsTransportIntegrationTests.cs`

**Mutation impact:** Reads convert Google `ValueRange.Values` to Part 1 mapper inputs and retain original one-based row numbers. Writes convert immutable Part 1 plans to Google requests. Numeric sheet IDs are request-local metadata, never persisted or treated as schema authority.

**Step 1: Write failing unit tests**

With a fake service boundary, cover quoted A1 worksheet names, exact four-sheet reads, row-number preservation including blank/trailing cells, missing range/sheet, duplicate sheet title, reordered/missing header, malformed row, maps/NPC dictionary duplication, and failure mapping for 401, 403, 404, 429, 5xx, cancellation, and transport exceptions. Exercise both read paths: `ReadGameDataAsync` maps all four sheets, while `ReadOwnedRowsAsync` returns only owned rows but still fails when either the `Maps` or `NPCs` header has drifted. Batch tests assert:

- one `BatchUpdateSpreadsheetRequest` contains all spawn and warp operations;
- each Part 1 one-based delete row `n` becomes `StartIndex = n - 1`, `EndIndex = n`, `Dimension = "ROWS"`;
- deletes remain descending per sheet before appends;
- append rows use `RowData.Values`, `CellData.UserEnteredValue`, `ExtendedValue.StringValue`, and `Fields = "userEnteredValue"`;
- no request targets an unplanned row or another sheet ID.

**Step 2: Write the required real local transport integration test**

Start a disposable loopback HTTP server on an OS-assigned port. Construct a real `SheetsService` with `BaseUri` set to that server, a no-op credential initializer, and `DefaultExponentialBackOffPolicy = None`. Run `GoogleSheetsGateway.ReadGameDataAsync` and `ReplaceOwnedRowsAsync` through actual `ExecuteAsync(CancellationToken)` calls. The server must return realistic JSON for spreadsheet metadata and `values:batchGet`, then capture the actual batch-update POST path/body and return a success JSON response. Assert authorization/request headers, escaped range query values, mapped row numbers/data, exactly one mutation POST, descending zero-based deletes, and both sheet appends. Do not use a mocked `HttpMessageHandler` in this test.

**Step 3: Run red**

```bash
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj --filter 'FullyQualifiedName~GoogleBatchBuilder|FullyQualifiedName~GoogleSheetsGateway|FullyQualifiedName~GoogleSheetsTransportIntegration'
```

Expected: FAIL because gateway and request builder are absent.

**Step 4: Implement minimum**

Use `Spreadsheets.Get` with fields limited to sheet title/ID for metadata, then `Spreadsheets.Values.BatchGet` with row-major, unformatted values for generated column ranges. Convert API scalar values invariantly to strings before Part 1 mapping; restore omitted trailing cells as nulls up to the schema width. Both read methods validate all four required worksheet headers before mapping or returning rows; the Push preflight may omit non-header `Maps`/`NPCs` values but may not omit their header checks. Use `Spreadsheets.BatchUpdate`, not `Values.BatchUpdate`, so row deletions and appends are one atomic spreadsheet batch. Set the Google client's automatic backoff policy to `None`.

Map response-status failures into `GameDataGatewayException`. Mark explicit 429 rejection as `RequestWasRejected = true`; conservatively mark 5xx and transport/missing-response failures false for mutation safety. Preserve cancellation separately so pre-dispatch caller cancellation is not wrapped.

**Step 5: Run green**

```bash
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj --filter 'FullyQualifiedName~GoogleBatchBuilder|FullyQualifiedName~GoogleSheetsGateway|FullyQualifiedName~GoogleSheetsTransportIntegration'
```

**Step 6: Commit**

```bash
git add src/MapEditor.GameData.Google/Sheets tests/MapEditor.GameData.Google.Tests/Sheets
git commit -m "feat: add Google Sheets game data gateway"
```

## Task 5: Implement Pull/Push coordination, retries, conflicts, and ambiguous outcomes

**Files:**
- Create: `src/MapEditor.GameData/Sync/GameDataSyncCoordinator.cs`
- Create: `src/MapEditor.GameData/Sync/RetryPolicy.cs`
- Create: `tests/MapEditor.GameData.Tests/Sync/GameDataSyncCoordinatorTests.cs`
- Create: `tests/MapEditor.GameData.Tests/Fakes/ScriptedGameDataGateway.cs`

**Mutation impact:** The coordinator is the only component that promotes Pull data, advances a pushed baseline, or latches/clears ambiguity. The gateway remains stateless; UI remains absent. Inject delay/time abstractions so tests contain no wall-clock sleeps.

**Step 1: Write failing Pull tests**

Cover map catalog loading; successful Pull; unmatched/untitled selection remaining a caller concern; dirty-session rejection unless discard was explicitly approved; complete replacement after approved discard; unknown selected map ID; target-map filtering; and late failures in maps, NPCs, spawn headers/rows, and warp headers/rows. Assert the original session's IDs, rows, history flags, snapshots, and references are unchanged after every failure. Test read retries at attempts/delays `1, 250ms, 1s`, cancellation during delay, and no retries for permanent failures.

**Step 2: Write failing Push tests**

Cover Part 1 validation blocking before network access; unchanged order-insensitive remote data; duplicate multiplicity conflict; conflict result containing latest rows; Cancel making no change; Pull Instead returning an instruction without mutating state; Overwrite planning against the captured latest rows; one gateway mutation call; success promoting snapshots and calling `MarkPushed`; other-map rows never entering plans; Push preflight rejection when either `Maps` or `NPCs` headers changed after Pull; and the accepted race being limited to the conflict-read/write window.

Add retry/outcome cases: explicit rejected 429 retries up to three attempts; exhausted 429 remains dirty but not ambiguous; authentication/permission failures do not retry; 503, timeout, connection reset, cancellation after dispatch, and malformed/missing success each make the session dirty plus `RequiresPull`; a second Push performs no gateway call; successful Pull clears the latch; cancellation before dispatch does not latch.

**Step 3: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataSyncCoordinator'
```

Expected: FAIL because coordinator and retry policy are absent.

**Step 4: Implement minimum**

Compose `GameDataValidator`, `SnapshotComparer`, and `ReplacementPlanner`; do not duplicate their logic. Reads execute through the bounded retry policy and publish only complete immutable results. Push validates first, reads latest owned rows, compares both multisets to pull snapshots, and returns conflict before building a write unless Overwrite was explicitly supplied with that conflict. A successful no-op Push may mark the current local state pushed without calling Google only after the conflict read confirms the remote baseline. Catch uncertain post-dispatch outcomes, latch `RequiresPull`, and never call `MarkPushed` without confirmed success.

**Step 5: Run green and regression suite**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataSyncCoordinator'
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj
```

**Step 6: Commit**

```bash
git add src/MapEditor.GameData/Sync tests/MapEditor.GameData.Tests/Sync \
  tests/MapEditor.GameData.Tests/Fakes/ScriptedGameDataGateway.cs
git commit -m "feat: coordinate safe Sheets pull and push"
```

## Task 6: Compose connectivity lazily and add the setup guide

**Files:**
- Modify: `src/MapEditor.App/MapEditor.App.csproj`
- Modify: `src/MapEditor.App/App.axaml.cs`
- Create: `src/MapEditor.App/Connectivity/GameDataConnectivity.cs`
- Modify: `tests/MapEditor.App.Tests/AppStartupTests.cs`
- Modify: `build-map-editor.sh`
- Modify: `tests/build-map-editor-script-tests.sh`
- Create: `docs/map-editor-google-sheets-setup.md`

**Mutation impact:** The application graph gains one application-wide connectivity owner holding settings, lazy OAuth connection/service factory, and domain coordinator. Startup must not read credentials, open a browser, create token files, or make HTTP calls. Existing window/workspace/dialog constructor signatures stay unchanged for Part 3 compatibility.

**Step 1: Write failing composition tests**

Inject fake connection/gateway factories into `ComposeMainWindow` or `GameDataConnectivity`. Assert one shared connectivity instance, settings identity preservation, lazy zero-call startup, canonical remembered URL retrieval, and no change to workspace/dialog ownership. Assert missing OAuth client JSON does not prevent startup and is reported only when Connect is requested. Extend the shell build tests to prove a release build fails before publishing when its configured OAuth JSON is absent, copies it into every staged application beside the executable when present, and never copies the source path or token directory.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AppStartup'
```

Expected: FAIL because connectivity is not composed.

**Step 3: Implement minimum composition**

Reference both connectivity projects from `MapEditor.App`. Add `GameDataConnectivity` as a non-Avalonia application service and expose it from `ComposedEditor`; create it lazily in `ComposeMainWindow`. Do not modify `WorkspaceViewModel`, `MapDocumentViewModel`, `IEditorDialogs`, `AvaloniaEditorDialogs`, `EditorDialogsProxy`, `FakeEditorDialogs`, `MainWindow`, or any XAML. Part 3 will invoke and present it.

Update `build-map-editor.sh` to require a release-only OAuth source path (for example `GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT`) and copy that JSON as `google-oauth-client.json` into each staged application before archiving. Local development may continue to use the absolute environment-variable path. The OAuth JSON is supplied by the release environment, shipped in release artifacts as approved, and never committed to Git. Validate the source as desktop-client JSON before any publish/archive replacement so a bad configuration leaves previous successful artifacts intact.

**Step 4: Write the setup guide**

Document, with no real IDs/secrets:

1. create/select a Google Cloud project and enable Google Sheets API;
2. configure OAuth consent, explaining Internal versus External testing/production and trusted test users;
3. create a Desktop app OAuth client and obtain its JSON;
4. supply it as an absolute `GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT` path for development and release builds; explain that the release script validates and packages it as `google-oauth-client.json` beside each executable, so end users receive the one configured desktop client without installing a separate file;
5. share the target spreadsheet directly with each signing-in account and paste its normal `docs.google.com/spreadsheets/d/...` URL;
6. explain first browser sign-in, loopback firewall/browser behavior, Sheets read/write scope, and that client IDs are not service-account credentials;
7. list platform settings and sibling `google-tokens` locations, and distinguish remembered URL from refresh tokens;
8. explain Disconnect (local token deletion) versus Google Account third-party-access revocation;
9. troubleshoot unverified-app/test-user, redirect/loopback, API-disabled, 401 reauthorization, 403 sharing/permission, 404 wrong URL, quota/rate-limit, and schema/header errors;
10. state that OAuth JSON, tokens, and machine paths must never be committed and that live verification uses a disposable shared spreadsheet.

**Step 5: Run green and full verification**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AppStartup'
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
git status --short
```

**Step 6: Commit**

```bash
git add src/MapEditor.App/MapEditor.App.csproj src/MapEditor.App/App.axaml.cs \
  src/MapEditor.App/Connectivity tests/MapEditor.App.Tests/AppStartupTests.cs \
  build-map-editor.sh tests/build-map-editor-script-tests.sh \
  docs/map-editor-google-sheets-setup.md
git commit -m "docs: wire and document Sheets connectivity"
```

## Final red-team verification

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
git status --short
```

Confirm manually from the diff:

- only `MapEditor.GameData.Google` references Google packages;
- `MapEditor.GameData` references neither Google nor Avalonia and coordinators contain no dialog/UI decisions;
- dialogs, workspace, document view models, window code, and XAML are unchanged except the composition root named in Task 6;
- `DefaultExponentialBackOffPolicy` is `None` for every `SheetsService` created by the application;
- the loopback integration test uses a real socket and real Google request serialization;
- settings contain only the canonical spreadsheet URL, never OAuth client JSON or tokens;
- all Google row deletes convert Part 1 one-based row numbers exactly once;
- each Push mutation is one `Spreadsheets.BatchUpdate` and never an automatically retried request;
- ambiguous outcomes preserve dirty data and block Push until successful Pull;
- no credential, token, temporary package project, or machine-specific path is tracked.
