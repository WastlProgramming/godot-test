# JumpRun – Schnittstellen-Vertrag (verbindlich)

Dieser Vertrag ist bindend für Entwickler und Designer. Änderungen nur über den Projektmanager; beide Seiten müssen ihn exakt einhalten (Pfade, Knotennamen, Klassennamen sind case-sensitive). Gameplay-Hintergrund: `docs/GDD.md`.

## 1. Technische Basis

- Godot 4.6.3 .NET, Binary (headless-fähig): `/home/bichl/git/godot-test/.tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64`. Der Ordner `.tools/` wird **nicht** verändert.
- `JumpRun.csproj`:
  ```xml
  <Project Sdk="Godot.NET.Sdk/4.6.3">
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <EnableDynamicLoading>true</EnableDynamicLoading>
      <RootNamespace>JumpRun</RootNamespace>
      <AssemblyName>JumpRun</AssemblyName>
      <Nullable>enable</Nullable>
      <LangVersion>12</LangVersion>
      <!-- Auf der Build-Maschine ist nur die .NET-10-Runtime installiert -->
      <RollForward>LatestMajor</RollForward>
    </PropertyGroup>
  </Project>
  ```
  `net8.0` ist für Godot 4.6.3 gültig (Mindestversion); der Referenz-Pack `microsoft.netcore.app.ref 8.0.28` liegt bereits im NuGet-Cache. Ein Wechsel auf `net9.0` ist nicht nötig.
- `NuGet.config` im Projektroot mit lokaler Quelle `.tools/godot/GodotSharp/Tools/nupkgs` (zusätzlich nuget.org), damit `Godot.NET.Sdk 4.6.3` offline auflösbar ist.
- `project.godot`: `config/name="JumpRun"`, `config/features=PackedStringArray("4.6", "C#", "Forward Plus")`, `[dotnet] project/assembly_name="JumpRun"`, `run/main_scene="res://scenes/ui/MainMenu.tscn"`.
- Szenen/Ressourcen im Godot-4-Textformat (`format=3`), `ext_resource` ohne `uid` ist erlaubt (nur `path` + `type` + `id`).
- Keine externen Assets: Meshes prozedural (BoxMesh, CylinderMesh, SphereMesh, PrismMesh, CSG*), Materialien als `.tres`, Shader als `.gdshader`, Icons als handgeschriebene `.svg`.
- Alle C#-Skriptklassen: `public partial class`, Dateiname == Klassenname, Namespace nach Ordner (`JumpRun.Player`, `JumpRun.World`, `JumpRun.Items`, `JumpRun.UI`, `JumpRun.Game`, `JumpRun.Tests`).

## 2. Ordnerstruktur und Zuständigkeit

```
/home/bichl/git/godot-test/
├── project.godot            DEV
├── JumpRun.csproj           DEV
├── JumpRun.sln              DEV
├── NuGet.config             DEV
├── icon.svg                 DESIGN (Projekticon, optional)
├── scripts/**               DEV
├── scenes/Main.tscn         DEV
├── scenes/Player.tscn       DEV
├── scenes/Crate.tscn        DEV
├── scenes/Pickup.tscn       DEV
├── scenes/objects/*.tscn    DEV   (Bridge.tscn, Trampoline.tscn, GrappleAnchor.tscn, Checkpoint.tscn)
├── scenes/ui/**             DESIGN (HUD.tscn, MainMenu.tscn, PauseMenu.tscn)
├── art/**                   DESIGN (materials, shaders, env, ui, fx)
├── tests/**                 DEV
└── docs/**                  PM (GDD, CONTRACT, TASKS); TESTER → TEST_REPORT.md; REVIEWER → REVIEW.md
```

Regel: Niemand schreibt in Dateien der anderen Rolle. Designer-UI-Szenen hängen die Skripte unter den Vertragspfaden an (`res://scripts/UI/HUD.cs` usw.), die der Entwickler erstellt. Der Entwickler referenziert Designer-Ressourcen ausschließlich über die Pfade in Abschnitt 7 und muss robust sein, falls eine Datei fehlt (Fallback, siehe 7.6).

## 3. Konstanten (`scripts/Game/GameConstants.cs`, `public static class GameConstants`)

| Name | Typ | Wert | Bedeutung |
|---|---|---|---|
| `ChunkLength` | float | 40f | nominelle Chunk-Länge entlang -Z (m) |
| `ChunksAhead` | int | 6 | Chunks, die vor dem Spieler-Chunk existieren müssen |
| `MaxAliveChunks` | int | 12 | hartes Limit |
| `DespawnDistance` | float | 30f | Mindestabstand ExitPoint hinter Spieler für Despawn |
| `MaxLateralOffset` | float | 12f | max. |X| der Chunk-Mittellinie |
| `MinWorldY` / `MaxWorldY` | float | -10f / 40f | Höhenbereich der Plattformen |
| `OriginShiftThreshold` | float | 1000f | Floating-Origin-Schwelle |
| `WalkSpeed` | float | 7f | |
| `SprintSpeed` | float | 11f | |
| `GroundAccel` / `AirAccel` | float | 60f / 24f | m/s² |
| `Gravity` | float | 24f | m/s² |
| `JumpVelocity` | float | 10.5f | ≈ 2,3 m Sprunghöhe |
| `CoyoteTime` / `JumpBufferTime` | float | 0.12f / 0.12f | s |
| `MaxSafeGap` | float | 6.0f | max. Lücke ohne Item |
| `MaxSafeStep` | float | 2.0f | max. Höhenstufe ohne Item |
| `FallDeathDepth` | float | 20f | unter Checkpoint-Y → Respawn |
| `AttackRange` / `AttackCooldown` | float | 2.2f / 0.35f | |
| `CrateBonusScore` | int | 25 | |
| `MaxItemCharges` | int | 5 | pro Item |
| `MaxPlaceables` | int | 3 | Brücken + Trampoline gleichzeitig |
| `BridgeLength` / `BridgeWidth` / `BridgeLifetime` | float | 16f / 2f / 20f | |
| `TrampolineBounceVelocity` / `TrampolineLifetime` | float | 22f / 30f | |
| `GrappleRange` / `GrappleConeDeg` / `GrapplePullSpeed` / `GrappleReleaseDistance` | float | 28f / 25f / 22f / 1.5f | |
| `DifficultyDistance` | float | 1500f | Distanz für d = 1 |
| `MouseSensitivityDefault` | float | 0.25f | Bereich 0.05–1.0 |
| `HighscoreFile` | string | `"user://highscore.json"` | |
| `SettingsFile` | string | `"user://settings.json"` | |
| `MaxSaveFileBytes` | int | 4096 | |
| `MaxScoreValue` | int | 10_000_000 | |

## 4. Physik-Ebenen (in `project.godot` unter `[layer_names]` als `3d_physics/layer_N` benennen)

| Ebene | Name | Bitwert | Wer liegt darauf |
|---|---|---|---|
| 1 | `world` | 1 | Plattformen, Wände, bewegliche Plattformen |
| 2 | `player` | 2 | Spieler (CharacterBody3D) |
| 3 | `crate` | 4 | Kisten (StaticBody3D) |
| 4 | `pickup` | 8 | Pickups (Area3D) |
| 5 | `trigger` | 16 | Checkpoints, Trampolin-Sprungfläche (Area3D) |
| 6 | `grapple_anchor` | 32 | Ankerpunkte |
| 7 | `placeable` | 64 | Brücke, Trampolin-Körper |

Masken: Spieler `collision_mask = 1|4|64 = 69`. Kiste: layer 4, mask 0. Pickup: layer 8, mask 2. Checkpoint/Trampolin-Area: layer 16, mask 2. Anker: layer 32, mask 0. Placeable: layer 64, mask 0. Enterhaken-Sichtlinien-Raycast: mask `1|32|64`. Kisten-Schlag-Query: mask 4. Kamera-SpringArm: mask 1|64.

## 5. Knotengruppen

| Gruppe | Mitglieder |
|---|---|
| `player` | der `PlayerController`-Knoten (genau einer) |
| `chunk` | jeder `Chunk`-Wurzelknoten |
| `crate` | jede `Crate` |
| `pickup` | jedes `Pickup` |
| `grapple_anchor` | jeder `GrappleAnchor` |
| `placeable` | platzierte Brücken und Trampoline |
| `checkpoint` | jeder `Checkpoint` |

## 6. Klassen, Verantwortung, öffentliche API

Signale werden mit `[Signal] public delegate void XEventHandler(...)` deklariert. Nur die hier genannten öffentlichen Mitglieder sind Vertrag; private Hilfen sind frei.

### 6.1 `scripts/Game/`

**`GameManager : Node`** (Autoload `GameManager`, Pfad `/root/GameManager`)
- Hält Run-Zustand, Score, Highscore, Seed, Einstellungen; lädt/speichert über `SaveSystem`.
- `public static GameManager Instance { get; }`
- Properties: `int Seed`, `int Score` (get), `int Distance` (get), `int Highscore` (get), `int CrateBonus` (get), `int Falls` (get), `float OriginOffsetZ` (get), `float MouseSensitivity` (get/set, geklemmt), `bool InvertY` (get/set), `float Difficulty` (get, 0..1).
- Methoden: `void StartNewRun(int? seed = null)`, `void ReportPlayerZ(float localZ)` (rechnet Distanz = max(-(localZ) + OriginOffsetZ)), `void AddOriginOffset(float dz)`, `void AddCrateBonus(int points)`, `void RegisterFall()`, `void SaveAll()`, `void GoToMainMenu()`, `void RestartRun()`, `void QuitGame()`.
- Signale: `ScoreChanged(int score, int distance)`, `HighscoreChanged(int highscore)`, `PlayerRespawned()`.
- Szenenpfade als Konstanten: `MainScenePath = "res://scenes/Main.tscn"`, `MainMenuScenePath = "res://scenes/ui/MainMenu.tscn"`.

**`SaveSystem`** (`public static class`, kein Node)
- `HighscoreData LoadHighscore()`, `bool SaveHighscore(HighscoreData data)`, `SettingsData LoadSettings()`, `bool SaveSettings(SettingsData data)`.
- `internal static HighscoreData ParseHighscore(string json)` / `ParseSettings(string json)` (für Tests, nie werfend).
- `HighscoreData` (record/class): `int Version=1, int Highscore, int BestDistance`. `SettingsData`: `int Version=1, float MouseSensitivity, bool InvertY`.
- Regeln: Größe ≤ `MaxSaveFileBytes`, `System.Text.Json` in try/catch, Clamping, atomar schreiben (`.tmp` → `DirAccess.RenameAbsolute`), nur feste `user://`-Pfade.

**`GameConstants`** (siehe Abschnitt 3).

**`Main : Node3D`** (`scripts/Game/Main.cs`, Wurzel von `Main.tscn`)
- Verdrahtet Spieler, WorldGenerator, HUD, PauseMenu; ruft beim Start `GameManager.Instance.StartNewRun()` falls kein Run aktiv; fängt die Maus.

### 6.2 `scripts/Player/`

**`PlayerController : CharacterBody3D`** (Gruppe `player`)
- Bewegung (kamerarelativ), Sprint, Sprung mit Coyote-Time/Jump-Buffer/variabler Höhe, Kistenschlag, Item-Einsatz über `Inventory`, Fallcheck + Respawn.
- Properties: `Inventory Inventory { get; }`, `bool IsGrappling { get; }`, `Vector3 RespawnPosition { get; }`, `bool DebugAutoRun { get; set; }` (Testmodus: läuft automatisch vorwärts und springt bei Kanten; nur für Smoke-Test).
- Methoden: `void SetCheckpoint(Vector3 position, int chunkIndex)`, `void Respawn()`, `void ApplyBounce(float verticalVelocity)`, `void StartGrapple(GrappleAnchor anchor)`, `void StopGrapple(bool keepMomentum)`, `void ApplyOriginShift(Vector3 offset)`.
- Signale: `Jumped()`, `Landed()`, `CrateHit(Crate crate)`, `Respawned()`, `CheckpointReached(int chunkIndex)`, `ItemUseFailed(string reason)` (reason ist deutscher Hinweistext für das HUD).
- `int ActiveCheckpointChunk { get; }`, `int CurrentChunkIndex { get; set; }` (vom WorldGenerator gesetzt).

**`ThirdPersonCamera : Node3D`** (Kamera-Rig, `top_level = true`, folgt dem Spieler weich)
- Struktur in `Player.tscn`: `Player/CameraRig (ThirdPersonCamera)/Pitch (Node3D)/SpringArm3D/Camera3D`.
- Methoden/Props: `Camera3D Camera { get; }`, `Vector3 FlatForward { get; }` (horizontale Blickrichtung, normalisiert), `void SnapBehind(Vector3 forward)`, `void ApplyOriginShift(Vector3 offset)`.

### 6.3 `scripts/World/`

**`WorldGenerator : Node3D`** (in `Main.tscn` als `Main/WorldRoot/WorldGenerator`; Chunks werden Kinder von `Main/WorldRoot/Chunks`)
- Chunk-Queue, Spawn/Despawn nach GDD 5.2, Floating Origin nach GDD 5.3, ermittelt Spieler-Chunk-Index, ruft `RestockSupply()` beim Respawn in Item-Chunks.
- Exports: `[Export] NodePath PlayerPath`, `[Export] int SeedOverride = -1`.
- Properties: `int AliveChunkCount { get; }`, `int HighestChunkIndex { get; }`, `IReadOnlyList<Chunk> AliveChunks { get; }`.
- Methoden: `void Reset(int seed)`, `Chunk? GetChunk(int index)`.
- Signale: `ChunkSpawned(int index)`, `ChunkDespawned(int index)`, `OriginShifted(Vector3 offset)`.

**`ChunkBuilder`** (`public sealed class`, kein Node, reine Logik + Node-Erzeugung)
- `ChunkBuilder(int seed)`, `Chunk Build(int index, Vector3 entryPoint, PatternType? previousPattern, int samePatternStreak, bool previousWasItemChunk, float difficulty)`.
- `static PatternType ChoosePattern(Random rng, int index, float difficulty, PatternType? previous, int samePatternStreak, bool previousWasItemChunk)` (testbar).
- Erzeugt Plattformen als `StaticBody3D` + `MeshInstance3D` (BoxMesh) + `CollisionShape3D` (BoxShape3D), Material über `MaterialLibrary`.

**`PatternType`** (`public enum`): `Start, GapJump, Stairs, ZigZag, MovingPlatforms, NarrowBeams, HeightJump, BigGap, TallWall`. Hilfsmethode `PatternTypeExtensions.IsItemRequired(this PatternType)` und `RequiredItems(this PatternType) → ItemType[]`.

**`Chunk : Node3D`** (Gruppe `chunk`, Knotenname `Chunk_{Index}`)
- Props: `int Index`, `PatternType Pattern`, `Vector3 EntryPoint`, `Vector3 ExitPoint`, `Aabb WorldBounds` (in globalen Koordinaten, aktualisiert nach Origin-Shift), `Vector3 CheckpointPosition`, `bool IsItemChunk`.
- Methoden: `void RestockSupply()`, `bool ContainsPointXZ(Vector3 globalPos)`, `void ApplyOriginShift(Vector3 offset)` (aktualisiert gecachte Punkte; die Node-Position verschiebt der Generator).

**`MovingPlatform : AnimatableBody3D`** (`sync_to_physics = true`)
- Exports: `Vector3 Offset` (Weg relativ zur Startposition), `float Speed`, `float PhaseOffset`. Ping-Pong per Sinus.

**`Checkpoint : Area3D`** (Gruppe `checkpoint`, Szene `res://scenes/objects/Checkpoint.tscn`)
- `int ChunkIndex`, `bool IsActive`; bei `BodyEntered(player)` → `PlayerController.SetCheckpoint(...)`, Flaggen-Material wechselt.

**`GrappleAnchor : StaticBody3D`** (Gruppe `grapple_anchor`, layer 32, Szene `res://scenes/objects/GrappleAnchor.tscn`)
- `void SetHighlighted(bool on)` (wechselt Material `grapple_anchor` ↔ `grapple_anchor_active`).

**`MaterialLibrary`** (`public static class`)
- `Material Get(string name)` lädt `res://art/materials/{name}.tres` (Cache); fehlt die Datei → Fallback-`StandardMaterial3D` (Magenta `#FF00FF`) + einmalige `GD.PushWarning`. `name` nur aus festen Konstanten (`MaterialNames`-Klasse), nie aus Benutzereingabe.

**`FrustumUtil`** (`public static class`)
- `bool AabbIntersectsFrustum(Aabb box, Godot.Collections.Array<Plane> frustum)` – konservativer Ebenen-Test (p-Vertex). Frustum über `Camera3D.GetFrustum()`.

### 6.4 `scripts/Items/`

**`ItemType`** (`public enum`): `None = 0, Bridge = 1, Trampoline = 2, GrappleHook = 3`. Slot-Index = `(int)type - 1`.

**`Inventory : Node`** (Kind von `Player`, Knotenname `Inventory`)
- `int GetCount(ItemType t)`, `bool Add(ItemType t, int amount = 1)` (false, wenn voll), `bool Consume(ItemType t)`, `ItemType Selected { get; }`, `void Select(ItemType t)`, `void SelectNext(int dir)`.
- Signale: `InventoryChanged(int bridge, int trampoline, int grapple)`, `SelectionChanged(int itemType)`.

**`Crate : StaticBody3D`** (Gruppe `crate`, Szene `res://scenes/Crate.tscn`)
- `ItemType? ForcedDrop { get; set; }`, `bool IsSupply { get; set; }`, `void Hit(PlayerController by)` (zerstört, spawnt Effekt `res://art/fx/crate_break.tscn` falls vorhanden, spawnt `Pickup` als Geschwister im Chunk, meldet Bonus an `GameManager`).
- `static ItemType RollDrop(Random rng)` (Drop-Tabelle GDD 9; `None` = kein Pickup).
- Signal: `Broken(int droppedItemType)`.

**`Pickup : Area3D`** (Gruppe `pickup`, Szene `res://scenes/Pickup.tscn`)
- `ItemType Item { get; set; }`; setzt Material `pickup_bridge|pickup_trampoline|pickup_grapple`; schwebt/rotiert; bei Spieler-Kontakt `Inventory.Add` → bei Erfolg `QueueFree`.

**`BridgeItem`** / **`TrampolineItem`** / **`GrappleHook`** (`public static class` oder `Node`, frei, aber mit diesen Einstiegspunkten):
- `static bool BridgeItem.TryUse(PlayerController p, out string failReason)`
- `static bool TrampolineItem.TryUse(PlayerController p, out string failReason)`
- `static bool GrappleHook.TryUse(PlayerController p, out string failReason)` und `static GrappleAnchor? GrappleHook.FindTarget(PlayerController p)` (für Highlight jeden Frame).
- Platzierte Objekte: `Bridge : StaticBody3D` (`scripts/Items/Bridge.cs`, Szene `res://scenes/objects/Bridge.tscn`) und `Trampoline : StaticBody3D` (`scripts/Items/Trampoline.cs`, Szene `res://scenes/objects/Trampoline.tscn`, enthält Area3D `BounceArea`), beide Gruppe `placeable`, Eigenschaft `float Lifetime`, Kinder von `Main/WorldRoot/Placeables`.
- Fehlertexte (deutsch, kurz): „Nur am Boden möglich“, „Kein Platz für das Trampolin“, „Kein Anker in Reichweite“, „Keine Ladung mehr“.

### 6.5 `scripts/UI/` (Skripte vom Entwickler, Szenen vom Designer)

**`HUD : Control`** – Wurzel von `res://scenes/ui/HUD.tscn`. Verbindet sich in `_Ready` per Gruppe `player` und `GameManager.Instance`.
- Methoden: `void ShowHint(string text, float seconds = 2f)`, `void FlashRespawn()`.
- Erwartete Knoten (alle mit **Unique Name** `%`, d. h. `unique_name_in_owner = true`):

| Unique Name | Typ | Zweck |
|---|---|---|
| `%DistanceLabel` | Label | „1234 m“ |
| `%ScoreLabel` | Label | „Punkte: 1259“ |
| `%HighscoreLabel` | Label | „Rekord: 2000“ |
| `%ItemSlot1` | PanelContainer | Slot Brücke |
| `%ItemSlot2` | PanelContainer | Slot Trampolin |
| `%ItemSlot3` | PanelContainer | Slot Enterhaken |
| `%ItemSlot1Count`, `%ItemSlot2Count`, `%ItemSlot3Count` | Label | Anzahl „x3“ |
| `%ItemSlot1Icon`, `%ItemSlot2Icon`, `%ItemSlot3Icon` | TextureRect | Icon |
| `%Crosshair` | Control (beliebig, z. B. TextureRect/Panel) | Bildschirmmitte |
| `%HintLabel` | Label | Hinweistexte, standardmäßig `visible = false` |
| `%RespawnFlash` | ColorRect | Vollbild, `mouse_filter = 2`, `modulate.a = 0` |

  Auswahl-Hervorhebung: `HUD.cs` setzt auf dem gewählten Slot `ThemeTypeVariation = "ItemSlotSelected"`, auf den anderen `"ItemSlot"`.

**`MainMenu : Control`** – Wurzel von `res://scenes/ui/MainMenu.tscn`.
- Knoten: `%StartButton` (Button), `%QuitButton` (Button), `%HighscoreLabel` (Label), `%SeedEdit` (LineEdit, `max_length = 9`, Platzhalter „Seed (optional)“).
- `MainMenu.cs` validiert Seed (nur Ziffern, sonst zufällig) und startet `GameManager.StartNewRun(seed)` + Szenenwechsel.

**`PauseMenu : Control`** – Wurzel von `res://scenes/ui/PauseMenu.tscn`, `process_mode = 3` (Always), standardmäßig `visible = false`.
- Knoten: `%ResumeButton`, `%RestartButton`, `%MainMenuButton`, `%QuitButton` (Button), `%SensitivitySlider` (HSlider, min 0.05, max 1.0, step 0.05).
- Methoden: `void Toggle()`, `bool IsOpen { get; }`.

Die Designer-Szenen enthalten die Skriptreferenz exakt so:
```
[ext_resource type="Script" path="res://scripts/UI/HUD.cs" id="1_script"]
```
(analog `MainMenu.cs`, `PauseMenu.cs`) und setzen `theme = ExtResource(...)` auf `res://art/ui/theme.tres` an der Wurzel.

## 7. Designer-Lieferungen (exakte Pfade)

### 7.1 Materialien `res://art/materials/` (Typ `StandardMaterial3D` oder `ShaderMaterial`; Entwickler nutzt nur Basistyp `Material`)

Plattformen je Muster:
`platform_start.tres`, `platform_gap.tres`, `platform_stairs.tres`, `platform_zigzag.tres`, `platform_moving.tres`, `platform_beam.tres`, `platform_height.tres`, `platform_biggap.tres`, `platform_wall.tres`, `platform_supply.tres`, `platform_checkpoint.tres`

Objekte:
`crate.tres`, `bridge.tres` (leicht transparent/leuchtend), `trampoline_base.tres`, `trampoline_pad.tres`, `grapple_anchor.tres`, `grapple_anchor_active.tres` (Emission stärker), `grapple_rope.tres`, `pickup_bridge.tres`, `pickup_trampoline.tres`, `pickup_grapple.tres`, `checkpoint_flag.tres`, `checkpoint_flag_active.tres`, `player_body.tres`, `player_accent.tres`

Zuordnung PatternType → Material: Start→`platform_start`, GapJump→`platform_gap`, Stairs→`platform_stairs`, ZigZag→`platform_zigzag`, MovingPlatforms→`platform_moving`, NarrowBeams→`platform_beam`, HeightJump→`platform_height`, BigGap→`platform_biggap`, TallWall→`platform_wall` (Wand) + `platform_biggap` (Bodenplatten); Versorgungsplattform→`platform_supply`; Checkpoint-Pad→`platform_checkpoint`.

Hinweis: Materialien müssen auf beliebig skalierten BoxMeshes gut aussehen → bevorzugt Triplanar (`uv1_triplanar = true`) oder Shader mit Weltkoordinaten; keine Texturdateien nötig.

### 7.2 Shader `res://art/shaders/*.gdshader` (optional, frei benennbar, nur von Designer-Materialien referenziert).

### 7.3 Umgebung
- `res://art/env/world_environment.tres` – `Environment` mit `ProceduralSkyMaterial`, Nebel (Ende ca. 180–220 m, passend zu 6 Chunks Sichtweite), Tonemapping, Glow optional. Entwickler setzt sie auf `Main/WorldEnvironment`.
- `res://art/env/Lighting.tscn` – Wurzel `Node3D` namens `Lighting` mit `DirectionalLight3D` namens `Sun` (Schatten an, `directional_shadow_max_distance` ≈ 120). Wird in `Main.tscn` als `Main/Lighting` instanziert. Das Licht folgt nicht dem Spieler (Richtungslicht).

### 7.4 UI
- `res://art/ui/theme.tres` – `Theme` mit Typvariationen: `ItemSlot`, `ItemSlotSelected` (Basis `PanelContainer`), `HudLabel`, `TitleLabel` (Basis `Label`), Button-Styles. Schriften nur `SystemFont` oder Default.
- Icons (handgeschriebenes SVG): `res://art/ui/icons/bridge.svg`, `res://art/ui/icons/trampoline.svg`, `res://art/ui/icons/grapple.svg`, `res://art/ui/icons/crosshair.svg`.
- Szenen: `res://scenes/ui/HUD.tscn`, `res://scenes/ui/MainMenu.tscn`, `res://scenes/ui/PauseMenu.tscn` mit Knoten exakt nach 6.5.

### 7.5 Effekte
- `res://art/fx/crate_break.tscn` – Wurzel `CPUParticles3D` (headless-sicher) mit `one_shot = true`, `emitting = false`; Entwickler instanziert, setzt `Emitting = true` und gibt nach 2 s frei.
- optional `res://art/fx/item_pickup.tscn` (gleiche Konvention).

### 7.6 Fallback-Pflicht des Entwicklers
Jede Designer-Ressource wird mit `ResourceLoader.Exists(path)` geprüft. Fehlt sie: Material → Magenta-Fallback, Effekt → weglassen, Icon → `null`, Environment → Default-`Environment`. Die UI-Szenen sind Pflicht für den Spielstart; im Smoke-Test darf ein fehlendes HUD nur eine Warnung erzeugen.

## 8. Szenenbäume des Entwicklers

`res://scenes/Main.tscn`:
```
Main (Node3D, Main.cs)
├── WorldEnvironment (Environment = art/env/world_environment.tres)
├── Lighting (Instanz art/env/Lighting.tscn)
├── WorldRoot (Node3D)
│   ├── WorldGenerator (Node3D, WorldGenerator.cs, PlayerPath = ../../Player)
│   ├── Chunks (Node3D)
│   └── Placeables (Node3D)
├── Player (Instanz scenes/Player.tscn)
└── UI (CanvasLayer)
    ├── HUD (Instanz scenes/ui/HUD.tscn)
    └── PauseMenu (Instanz scenes/ui/PauseMenu.tscn)
```

`res://scenes/Player.tscn`:
```
Player (CharacterBody3D, PlayerController.cs, Gruppe player, layer 2, mask 69)
├── CollisionShape3D (CapsuleShape3D r=0.4 h=1.8)
├── Visual (Node3D) → Body (MeshInstance3D Capsule, player_body), Visor (MeshInstance3D Box, player_accent)
├── Inventory (Node, Inventory.cs)
├── GrappleRope (MeshInstance3D Cylinder, grapple_rope, top_level, visible=false)
└── CameraRig (Node3D, ThirdPersonCamera.cs) → Pitch → SpringArm3D (length 5, mask 65) → Camera3D
```

## 9. Tests (`tests/**`, Entwickler)

- `res://tests/SmokeTest.tscn` + `tests/SmokeTest.cs` (`JumpRun.Tests.SmokeTest : Node`): lädt `Main.tscn`, aktiviert `PlayerController.DebugAutoRun`, simuliert ≥ 30 s, prüft: keine Exceptions, `AliveChunkCount` zwischen `ChunksAhead` und `MaxAliveChunks`, `HighestChunkIndex` steigt, Despawn ist erfolgt, Spieler-Chunk existiert stets. Ausgabe `SMOKE_TEST_PASS` bzw. `SMOKE_TEST_FAIL: <grund>`, Exit-Code 0/1 via `GetTree().Quit(code)`.
- `tests/LogicTests.cs` (aufgerufen aus derselben Testszene oder `res://tests/LogicTests.tscn`): SaveSystem mit kaputtem/zu großem/manipuliertem JSON; `ChoosePattern`-Regeln über 1000 Chunks (keine zwei Item-Chunks in Folge, keine Item-Chunks in 0–2); Lösbarkeit (jeder Item-Chunk hat ≥ 2 ForcedDrop-Kisten mit passendem Item); `Crate.RollDrop`-Verteilung.
- Befehle:
  ```
  dotnet build /home/bichl/git/godot-test/JumpRun.csproj
  .tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64 --headless --path /home/bichl/git/godot-test --import
  .tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64 --headless --path /home/bichl/git/godot-test res://tests/SmokeTest.tscn
  ```

## 10. Sicherheits- und Qualitätsregeln

- Keine Reflection-basierte Typerzeugung aus Daten, kein `OS.Execute`, keine Netzwerkzugriffe.
- Dateizugriffe nur auf `GameConstants.HighscoreFile` / `SettingsFile` (+ `.tmp`).
- Alle externen Eingaben (Savefiles, Seed-Feld) validieren und begrenzen.
- Freigaben nur per `QueueFree()`; vor Zugriff auf gespeicherte Node-Referenzen `GodotObject.IsInstanceValid` prüfen (Anker, Kisten, Chunks).
- Signale in `_ExitTree` trennen, wenn auf Autoload/andere Szenen verbunden wurde.
- Keine Allokationen pro Frame in heißen Pfaden, wo vermeidbar (Frustum-Array einmal pro Frame holen).
