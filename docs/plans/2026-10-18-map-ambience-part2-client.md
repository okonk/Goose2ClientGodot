# Map Ambience Part 2 (Client) Implementation Plan

**Goal:** Goose2ClientGodot renders the server's `AMB` ambience as a day/night tint, rain, snow,
storm, and fog, gives the local player a light that grows with darkness, and the map editor accepts
the new Maps schema.

**Architecture:** A per-map `AmbienceManager` node is created by `MapManager._Ready` inside the map
SubViewport. It listens for `MapAmbiencePacket`, drives a `CanvasModulate` from a pure, unit-tested
`AmbienceMath` helper, and owns the weather visuals. A `LocalPlayerLight` (`PointLight2D`) is
attached to the local character and reads `AmbienceManager.Darkness`. Everything is freed with the
map scene, so there's no reset logic.

**Tech Stack:** Godot 4.6 C# (GodotSharp 4.6.2), xUnit (`tests/Goose2Client.Tests`), headless
in-product self-test (`+selftest=…`).

Design: `illutiagooseserver/docs/plans/2026-10-18-map-ambience-design.md`.
**Depends on Part 1** (server worktree `illutiagooseserver/.worktrees/map-ambience`): the `AMB`
wire format (Part 1 Task 4) and the regenerated `tools/DataEditor/schema.json` (Part 1 Task 2).
Port source: `3dMMO-Server/client/Assets/Scripts/AmbienceManager.cs` and
`3dMMO-Server/client/Assets/Scripts/Entity/Character.cs:69-88,341-382,627-631`.

**Repo rule (AGENTS.md):** no new comments or doc strings unless the "why" is non-obvious. Expected
exceptions: the wire-format line on `MapAmbiencePacket` (matching `MapWarpPacket.cs:3`) and the
packed-color layout note in `AmbienceMath`.

---

## APIs verified

| API | Location |
|---|---|
| `PacketHandler` (`Prefix`, `Parse(PacketParser)`) | `Scripts/Network/PacketHandler.cs:6-12` |
| Example handler + wire-format comment | `Scripts/Network/Packets/MapWarpPacket.cs:1-21` |
| `PacketParser.GetInt32()` / `GetInt64()` (`Convert.ToInt64`) | `Scripts/Network/PacketParser.cs:63-71` |
| Prefix lookup tries prefixes **shortest first** (`A`, `AM`, `AMB`…); no `A`/`AM` handlers exist | `Scripts/Network/PacketManager.cs:35-44` |
| `PacketManager.Listen<T>(Action<object>)` / `Remove<T>` | `Scripts/Network/PacketManager.cs:12,24` |
| `PacketManager.Handle(string)` | `Scripts/Network/PacketManager.cs:46` |
| Packet unit-test style | `tests/Goose2Client.Tests/MapWarpPacketTests.cs:1-24` |
| Test project compiles all of `Scripts/**/*.cs` with GodotSharp (Godot structs usable; no engine) | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:8,14` |
| `Map.tscn`: root `SubViewport` → `World` (`MapManager`, y-sorted) → `Camera2D`, `Layers`, `Objects` (z 14), `Characters` (z 15) | `Scenes/Map.tscn:5-26` |
| `MapManager._Ready` (`_camera`, listener registration) and `_ExitTree` | `Scripts/MapManager.cs:66-129`, `Scripts/MapManager.cs:131-161` |
| `MapManager.AttachLocalPlayer(Character)` | `Scripts/MapManager.cs:266-272` |
| Character origin is tile-bottom (feet); standard sprite is 48 px tall | `Scripts/Character/CharacterAnchor.cs:7-14` |
| Packets queued during load are delivered after `MapManager._Ready` | `Scripts/GameManager.cs:346-356` (comment at `:349-353`) |
| Names/bubbles live in root `WorldTextBridge` CanvasLayer, outside the SubViewport | `Scripts/MapManager.cs:398-399`, `Scripts/GameManager.cs:221,329` |
| No CanvasLayers exist inside the map SubViewport today | `Scripts/MapManager.cs` (only `ObjectLayer` z-index 15, `:92`) |
| Headless self-test dispatch | `Scripts/GameManager.cs:263-272` |
| Self-test skeleton (`Run`, `Frame()`, `Assert`, `Quit(failed?1:0)`) | `Scripts/CharacterIconSelfTest.cs:8-40` |
| Self-test map scene + `PacketManager.Handle("MKC…")` | `Scripts/CharacterIconSelfTest.cs:133-150` |
| Self-test runner script | `tools/tests/run_character_icon.sh` |
| Map editor embedded schema resource | `src/MapEditor.GameData/MapEditor.GameData.csproj:7-8`, `src/MapEditor.GameData/Schema/GameDataSchema.cs:12` |
| Map editor checks Maps headers **by position** | `src/MapEditor.GameData/Schema/SchemaHeaderValidator.cs` |
| Map editor reads Maps columns by name only (`map_id`, `map_name`, `map_filename`) | `src/MapEditor.GameData/Rows/SheetRowMapper.cs:48-55` |
| 3dMMO texture + import settings (lossless, no VRAM compression) | `3dMMO-Server/client/Assets/Textures/light_gradient.png{,.import}` |

## Threading, lifecycle

- Everything runs on the Godot main thread. Packet observers are invoked from
  `PacketManager.Handle`, which `GameManager` calls on the main thread.
- **Lifecycle:** `AmbienceManager` is created in `MapManager._Ready` and is a child of `World`, so
  it's freed with the map scene. It registers its listener in `_Ready` and removes it in
  `_ExitTree`. **Adversarial risk:** a listener left registered after the map scene is freed would
  throw `ObjectDisposedException` on the next `AMB`. The self-test (Task 7) covers this.
- `LocalPlayerLight` is a child of the local `Character`, so it's freed with it. It holds a
  reference to the `AmbienceManager` from the same map scene, so both share a lifetime.
- `GD.Load` must not run in static initializers. `Scripts/**` is compiled into the xUnit project
  without an engine, so load the texture in `_Ready`.

---

### Task 1: Map editor schema

**Files:**
- Modify (generated copy): `src/MapEditor.GameData/Schema/game-data-schema.json` ← `illutiagooseserver/.worktrees/map-ambience/tools/DataEditor/schema.json` (from Part 1 Task 2)

**Mutation impact:**
- Source of truth: server `MapsCsvToSql` descriptors → SchemaGen `schema.json`.
- Readers: `GameDataSchema.LoadEmbedded()` → `SchemaHeaderValidator` (positional), `SheetRowMapper` (by name).
- Derived state: none in the client. Fakes build headers from the embedded schema.
- Invariant: the map editor accepts a Maps sheet with `weather`/`lighting` between
  `pets_enabled (1)` and `script path`. `map_id`/`map_name`/`map_filename` resolution is unchanged.
- Rollout: the user updates the live sheet **before** this build ships (design, Section 5).

**Steps:**
1. `cp ../../../illutiagooseserver/.worktrees/map-ambience/tools/DataEditor/schema.json src/MapEditor.GameData/Schema/game-data-schema.json`
   (or use the absolute path). `git diff` should show only the two new Maps columns.
2. Add a test to `tests/MapEditor.GameData.Tests/Schema/GameDataSchemaTests.cs`:
   `Maps_HasWeatherAndLightingBeforeScriptPath`. The column names around them should be, in order,
   `pets_enabled`, `weather`, `lighting`, `script_path`. Red before the copy, green after.
3. `for d in tests/MapEditor.*.Tests; do dotnet test $d; done` → all pass (baseline: App 1042,
   Core 377, GameData.Google 73, GameData 299, Rendering 464).
4. Commit `feat(map-ambience): map editor schema with weather and lighting columns`

---

### Task 2: `WeatherType` and `MapAmbiencePacket`

**Files:**
- Create: `Scripts/Network/Packets/MapAmbiencePacket.cs`, `Scripts/WeatherType.cs`
- Test: `tests/Goose2Client.Tests/MapAmbiencePacketTests.cs`

```csharp
public enum WeatherType : byte { Clear = 0, Rain = 1, Fog = 2, Storm = 3, Snow = 4 }

// Wire format: AMB<startAlpha‰>,<endAlpha‰>,<tint>,<fill>,<elapsedMs>,<durationMs>,<weather> — colors 0xRRGGBBAA as decimal (server: P.MapAmbience)
public class MapAmbiencePacket : PacketHandler
{
    public float StartAlpha, EndAlpha;      // ‰ / 1000f
    public uint TintColor, FillColor;       // (uint)GetInt64()
    public long ElapsedMs, DurationMs;
    public WeatherType Weather;             // undefined byte → Clear
    public override string Prefix { get; } = "AMB";
}
```

Use properties, matching `MapWarpPacket`. Weather: `GetInt32()`, then `Enum.IsDefined` check, else `Clear`.

**Tests** (same strings Part 1 asserts on the server):
- `Amb_ParsesOutsideRain`: `"AMB0,700,151587327,1321471,540000,1440000,1"` → 0f, 0.7f,
  `0x090909FF`, `0x001429FF`, 540000, 1440000, Rain.
- `Amb_ParsesNone`: `"AMB0,0,151587327,1321471,0,0,0"` → Clear, duration 0.
- `Amb_ColorAboveIntMax` (adversarial, fails if parsed with `GetInt32`): tint `4294967295` → `0xFFFFFFFF`.
- `Amb_UnknownWeather_IsClear`: weather `9` → Clear.

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~MapAmbiencePacketTests` red → green.
Commit `feat(map-ambience): AMB packet`

---

### Task 3: `AmbienceMath` (pure helper)

**Files:**
- Create: `Scripts/Map/AmbienceMath.cs`
- Test: `tests/Goose2Client.Tests/AmbienceMathTests.cs`

```csharp
public static class AmbienceMath
{
    public const float LocalLightIntensity = 0.6f;
    public static float TimeOfDay(long elapsedMs, long durationMs);           // 0..1; 0.5 when durationMs <= 0
    public static float Darkness(float startAlpha, float endAlpha, long elapsedMs, long durationMs);
    public static Color ModulateColor(uint tintRgba, float darkness);
    public static bool IsNight(float timeOfDay);                              // < 0.25 || > 0.75
}
```

Port from 3dMMO `AmbienceManager.cs` `UpdateDayNightCycle` / `UintToModulateColor`:
- `durationMs <= 0` → `Darkness = startAlpha`, `TimeOfDay = 0.5`.
- Otherwise `t = (elapsedMs % durationMs) / (float)durationMs`,
  `Darkness = Mathf.Lerp(start, end, 1 − Mathf.Sin(t · π))`.
- `ModulateColor`: `r = (tint >> 24 & 0xFF) / 255`, etc. Each channel is `1 − d + c · d`; alpha 1.
  The tint's own alpha byte is ignored. This is the one justified comment.

**Tests:**
- `Darkness_Noon_IsStart`, `Darkness_Midnight_IsEnd` (outside 0 → 0.7).
- `Darkness_StaticCycle_IsStartAlpha` (cave 0.7/0.7, duration 0 → 0.7).
- `Darkness_None_IsZero`, and `ModulateColor(tint, 0)` == `Colors.White` (adversarial: the
  none preset must render untinted).
- `ModulateColor_FullDark_IsTint`: `ModulateColor(0x090909FF, 1)` ≈ (9/255, 9/255, 9/255, 1).
- `ModulateColor_IgnoresTintAlpha`: `0x09090900` and `0x090909FF` give the same color.
- `TimeOfDay_WrapsElapsed`: elapsed 1 500 000 of 1 440 000 → 60 000/1 440 000.

Red → green → commit `feat(map-ambience): ambience math helper`

---

### Task 4: `AmbienceManager` (tint, state, lifecycle)

**Files:**
- Create: `Scripts/Map/AmbienceManager.cs`
- Modify: `Scripts/MapManager.cs` (`_Ready` after `_camera` is set, `:73`; expose `public AmbienceManager Ambience { get; private set; }`)

**API:**

```csharp
public partial class AmbienceManager : Node2D
{
    public float Darkness { get; private set; }
    public float TimeOfDay { get; private set; }
    public WeatherType CurrentWeather { get; private set; } = WeatherType.Clear;
    public bool IsNight => AmbienceMath.IsNight(TimeOfDay);
    public event Action? WeatherChanged;
    internal CanvasModulate TintNode { get; private set; } = null!;
    public void Setup(Camera2D camera);
}
```

**Behavior:**
- `MapManager._Ready`: `Ambience = new AmbienceManager { Name = "Ambience" }; AddChild(Ambience); Ambience.Setup(_camera);`
  It's added before listeners are registered, so it exists before queued packets drain.
- `_Ready`: create the `CanvasModulate` (color `Colors.White`) as a child, then
  `GameManager.Instance.PacketManager.Listen<MapAmbiencePacket>(OnAmbience)`. Set a `_listening` flag.
- `_ExitTree`: if `_listening`, call `Remove<MapAmbiencePacket>(OnAmbience)`, kill tweens, clear the flag.
- `OnAmbience`: store start/end/tint/duration. `_cycleStartMsec = Time.GetTicksMsec() − (ulong)ElapsedMs`.
  If the weather changed: set `CurrentWeather`, call `TransitionToWeather` (Task 5), invoke `WeatherChanged`.
- `_Process`: `elapsed = Time.GetTicksMsec() − _cycleStartMsec`; set `TimeOfDay` and `Darkness`
  from `AmbienceMath`; `TintNode.Color = AmbienceMath.ModulateColor(tint, Darkness)`.
- Defaults before any packet: `Darkness = 0`, color white. A map with no `AMB` looks exactly like today.

**Mutation impact:**
- Source of truth: the server's `MapAmbience`. The client copy is the last `MapAmbiencePacket`
  stored in `AmbienceManager` fields.
- Readers: `_Process` (CanvasModulate), `LocalPlayerLight` (`Darkness`, Task 6), weather visuals (Task 5).
- Propagation: packet → fields → next `_Process` writes `TintNode.Color` and `Darkness`. The
  weather path is packet → `TransitionToWeather` immediately.
- Invariant: no listener survives the map scene (proved in Task 7).

**Steps:** No headless-free unit test is possible for the node itself. Its math is covered by
Task 3 and its lifecycle by Task 7. Build: `dotnet build Goose2ClientGodot.csproj` → 0 errors;
`dotnet test tests/Goose2Client.Tests` → pass.
Commit `feat(map-ambience): ambience manager with day/night tint`

---

### Task 5: Weather visuals

**Files:**
- Modify: `Scripts/Map/AmbienceManager.cs`

Port 3dMMO `CreateWeatherParticles`, `CreateParticleTexture`, `TransitionToWeather`,
`UpdateLightning`, `FlashLightning`, with these Goose-specific changes:
- **Particles:** create them in `Setup(camera)` as children of the map's `Camera2D` (not
  `GetViewport().GetCamera2D()` polling). Use `ZIndex = 100` so they draw above `Characters` (z 15).
  Rain uses speed 800 and color (0.63, 0.77, 1, 0.7); snow uses 150 and (1, 1, 1, 0.8); storm uses
  1200 and (0.5, 0.6, 0.8, 0.8). `Amount = 800`, `Emitting = false`.
- **Extents from the viewport, not 1920/1200 constants:** `var size = GetViewport().GetVisibleRect().Size`
  (the map SubViewport). `EmissionRectExtents = new(size.X * 0.6f, 1)`,
  `Position = new(0, -size.Y / 2 − 64)`, `Lifetime = (size.Y + 128) / (speed * 0.8f)` so the
  slowest particle crosses the screen. `Preprocess = Lifetime`. Recompute is not needed on resize
  (accepted gap: corrected on the next map change).
- **Fog / lightning:** a `CanvasLayer { Layer = 100 }` child of the `AmbienceManager`. It's inside
  the SubViewport, so it sits over the world and under the HUD and `WorldTextBridge`. It holds two
  full-rect `ColorRect`s with `MouseFilter = Ignore`: fog (0.5, 0.5, 0.5, a) and flash (1, 1, 1, a).
- **Transition:** emitting flags per type, fog alpha tween to 0.3 or 0 over 2 s, lightning every
  5–15 s (`GD.RandRange`) only during Storm, flash 0.8 over 0.05 s then 0 over 0.1 s.
- `TransitionToWeather` is a no-op for nodes not yet created. `Setup` runs in `MapManager._Ready`
  before any packet, so this only matters for robustness.

Build + `dotnet test tests/Goose2Client.Tests`.
Commit `feat(map-ambience): rain, snow, storm and fog visuals`

---

### Task 6: Default local-player light

**Files:**
- Create: `Assets/Textures/light_gradient.png`, `Assets/Textures/light_gradient.png.import` (copied
  from 3dMMO; keep the lossless, non-VRAM import params)
- Create: `Scripts/Map/LocalPlayerLight.cs`
- Modify: `Scripts/MapManager.cs:266-272` (`AttachLocalPlayer`)

```csharp
public partial class LocalPlayerLight : PointLight2D
{
    public LocalPlayerLight(AmbienceManager ambience);
}
```

- `_Ready`: `Texture = GD.Load<Texture2D>("res://Assets/Textures/light_gradient.png")`,
  `BlendMode = Add`, `TextureScale = 80f / 128f`, `Color = new Color(0xFFF0E0FF)` (Godot's `Color(uint)`
  is RGBA), `Position = new(0, −24)` (half the standard 48 px sprite above the tile-bottom origin),
  `Energy = 0`.
- `_Process`: `Energy = AmbienceMath.LocalLightIntensity * _ambience.Darkness`.
- `AttachLocalPlayer`: if `c` has no `LocalPlayerLight` child, add `new LocalPlayerLight(Ambience)`.
  It's idempotent, because `AttachLocalPlayer` runs from both `OnMakeCharacter` (`:186`) and
  `OnSetYourCharacter` (`:209`).
- Other characters get no light (item lights are deferred).

**Mutation impact:** adds a child node to the local `Character`. Readers: Godot's renderer only.
No derived state found. Invariant: exactly one light, on the local player only (proved in Task 7).

Build. Open the editor once (or `godot --headless --import`) so `.godot/imported` is created; that
directory is not committed.
Commit `feat(map-ambience): default local player light`

---

### Task 7: Headless ambience self-test (integration)

**Files:**
- Create: `Scripts/AmbienceSelfTest.cs` (pattern: `Scripts/CharacterIconSelfTest.cs:8-40`)
- Modify: `Scripts/GameManager.cs:263-272` (dispatch `+selftest=ambience`)
- Create: `tools/tests/run_ambience.sh` (copy `tools/tests/run_character_icon.sh`; change the arg and messages)

Body, using the real `Map.tscn` and real packets through `gm.PacketManager.Handle`:
1. `gm.CurrentMap = MapDocument.Create(4, 4)`; instantiate `res://Scenes/Map.tscn` and add it to
   root; `await Frame()`; `mm = gm.CurrentMapManager`.
2. Assert `mm.Ambience` exists and its `CanvasModulate`'s viewport is the map SubViewport
   (`mm.Ambience.TintNode.GetViewport() == mapScene`). If it were in the root viewport, it would
   tint the HUD.
3. No packet yet → `TintNode.Color == Colors.White`.
4. `Handle("AMB700,700,151587327,1321471,0,0,0")` (cave), `await Frame()` → `Darkness ≈ 0.7`, color not white.
5. `Handle("AMB0,700,151587327,1321471,0,1440000,1")` → `CurrentWeather == Rain`, rain particles
   `Emitting`, snow not emitting. Then `…,4` → snow emitting, rain not. Then `…,2` → fog tween
   target is 0.3 (check after `await` ≥ 2.1 s, or check the tween's final value).
6. `Handle("MKC7,…")` (the MKC string from `CharacterIconSelfTest.cs:143`) then `Handle("SUC7")` →
   character 7 has exactly one `LocalPlayerLight`. Handle `SUC7` again → still exactly one
   (adversarial: idempotency). A second MKC character has none.
7. Cave packet again, `await Frame()` → the light's `Energy ≈ 0.6 * 0.7`.
8. **Lifecycle (adversarial):** `mapScene.QueueFree()`, `await Frame()` twice, then
   `Handle("AMB0,0,151587327,1321471,0,0,3")` must not throw. A leaked listener throws
   `ObjectDisposedException` on the freed node.

Run: `sh tools/tests/run_ambience.sh` → prints `[ambience_selftest] PASS`, exit 0. Red: before the
`_ExitTree` removal exists (or with it commented out locally), step 8 fails.
Commit `test(map-ambience): headless ambience self-test`

---

### Task 8: Full verification and manual smoke

1. `dotnet build Goose2ClientGodot.csproj` → 0 errors.
2. `dotnet test tests/Goose2Client.Tests` (baseline 1690) and every `tests/MapEditor.*.Tests` → all pass.
3. `sh tools/tests/run_ambience.sh` and `sh tools/tests/run_character_icon.sh` → PASS.
4. **Manual, with the Part 1 server:** set a map to `{"preset":"outside","cycleDuration":120}` /
   `{"Rain":0.5,"Snow":0.5}` in a local DB. Log in → the tint cycles over about 2 minutes. Then:
   `/settime night` → dark, with the player light visible; `/settime noon` → bright, light invisible;
   `/setweather Storm` → heavy rain plus flashes; `/setweather Fog` → grey overlay with readable
   names, HUD untinted. Warp to a blank map → untinted, no weather, no visible light.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| Client parses exactly what the server sends | `Amb_ParsesOutsideRain`, `Amb_ParsesNone` (same strings as Part 1 `BuildPacket_*`) |
| Colors above `int.MaxValue` survive | `Amb_ColorAboveIntMax` |
| `none` / no packet renders untinted | `Darkness_None_IsZero` + `ModulateColor(…, 0) == White`, self-test step 3 |
| Tint affects only the world, not HUD/names | self-test step 2 (modulate's viewport is the map SubViewport) |
| Weather switches the right visuals | self-test step 5 |
| Exactly one light, local player only | self-test step 6 |
| Light scales with darkness | self-test step 7 |
| No listener outlives the map scene | self-test step 8 |
| Map editor accepts the new Maps layout | `Maps_HasWeatherAndLightingBeforeScriptPath` + existing MapEditor suites |
