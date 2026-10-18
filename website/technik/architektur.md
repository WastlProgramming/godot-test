# Architektur für Entwickler

JumpRun ist ein Godot-4.6-Projekt in **C# (.NET 8)**. Alle Skripte liegen unter `scripts/`, Namespaces folgen den
Ordnern (`JumpRun.World`, `JumpRun.Items`, `JumpRun.Characters`, `JumpRun.Audio` …). Jede Skriptklasse ist
`public partial class`, Dateiname = Klassenname.

## Ordnerstruktur

```
project.godot            Projekteinstellungen, InputMap, Autoloads, Physik-Layer
default_bus_layout.tres  Audio-Busse Master / Music / SFX
JumpRun.csproj / .sln    C#-Projekt (Godot.NET.Sdk 4.6.3, net8.0)
run-linux.sh             Start unter Linux/WSL (OpenGL über Mesa-D3D12)
scenes/
  Main.tscn              Spielszene
  Player.tscn            Spieler (CharacterBody3D + Rig + Kamera)
  Backdrop.tscn          Hintergrund (Berge, Inseln, Wolkenmeer)
  Crate.tscn, Pickup.tscn
  objects/               Bridge, Trampoline, GrappleAnchor, Checkpoint, HelicopterRotor
  ui/                    MainMenu, CharacterSelect, HUD, PauseMenu
scripts/
  Game/                  GameManager, GameConstants, SaveSystem, Main
  Player/                PlayerController, ThirdPersonCamera, IPlayerAnimState
  World/                 WorldGenerator, ChunkBuilder, Chunk, ChunkLayout, PatternType, PlatformShape,
                         ShapeMeshFactory, MovingPlatform, RotatingPlatform, Checkpoint, GrappleAnchor,
                         FrustumUtil, MaterialLibrary, Backdrop, BackdropMeshes
  Items/                 ItemType, Inventory, Crate, Pickup, Placeables, BridgeItem/Bridge,
                         TrampolineItem/Trampoline, GrappleHook, HelicopterItem, HelicopterRotor
  Characters/            CharacterDefinition, CharacterCatalog, CharacterModelFactory, CharacterRig,
                         CharacterMaterials
  Audio/                 AudioManager, AudioEventBridge, Sfx
  UI/                    HUD, MainMenu, CharacterSelect, PauseMenu
art/                     Shader, Materialien (.tres), Charakter-Materialien, Audio (.wav), Umgebung, FX, UI
tools/                   gen_audio.py, verify_audio.py
tests/                   Headless-Tests (je Test eine .tscn + .cs)
docs/                    Projekt-Dokumente (GDD, Verträge, Aufgaben, Reviews)
website/                 Diese Dokumentation (VitePress)
```

## Autoloads

| Name | Klasse | Aufgabe |
|---|---|---|
| `GameManager` | `JumpRun.Game.GameManager` | Run-Zustand, Seed, Strecke, Punkte, Highscore, Einstellungen (Charakter, Lautstärken, Mausempfindlichkeit), Szenenwechsel |
| `AudioManager` | `JumpRun.Audio.AudioManager` | Musik mit Überblendung, SFX-Pools, Loops, Bus-Lautstärken |

`GameManager` wird vor `AudioManager` geladen. Beide stellen eine statische `Instance` bereit.

## Szenenbaum `Main.tscn`

```
Main (Node3D, Main.cs)
├── WorldEnvironment            art/env/world_environment.tres (Himmel, Nebel, Tonemap, Glow)
├── Lighting                    art/env/Lighting.tscn (Sun + Fill)
├── Backdrop                    scenes/Backdrop.tscn – nicht unter WorldRoot, wird nicht verschoben
├── WorldRoot (Node3D)
│   ├── WorldGenerator          WorldGenerator.cs
│   ├── Chunks                  hier hängen die Chunk-Knoten
│   └── Placeables              Gruppe placeables_root (Brücken, Trampoline)
├── Player                      scenes/Player.tscn
├── UI (CanvasLayer)
│   ├── HUD
│   └── PauseMenu
└── AudioEventBridge            Spieler-Signale → Sounds
```

`Main._Ready` richtet Umgebung und Licht ein (mit Fallbacks), startet bei Bedarf einen neuen Run
(`GameManager.StartNewRun`) und ruft `WorldGenerator.Reset(seed)` auf. Danach wird die Maus eingefangen.

### Spieler `Player.tscn`

```
Player (CharacterBody3D, PlayerController.cs, Gruppe player)
├── CollisionShape3D            Kapsel r = 0,4 m, h = 1,8 m (für alle Charaktere gleich)
├── Visual                      PlayerController dreht nur die Gier (Yaw)
│   └── Rig                     CharacterRig.cs – baut und animiert das Modell
├── Inventory                   Inventory.cs
├── GrappleRope                 Seil-Mesh
├── CameraRig                   ThirdPersonCamera.cs → Pitch → SpringArm3D → Camera3D (far = 1500)
└── HelicopterRotor             wird zur Laufzeit angehängt (scenes/objects/HelicopterRotor.tscn)
```

## Wichtige Klassen

### Welt

| Klasse | Aufgabe |
|---|---|
| `WorldGenerator` | Spawnt Chunks vorne (max. 1 pro Physik-Frame), despawnt hinten (Abstand + Frustum), Floating Origin, Restock nach Respawn |
| `ChunkBuilder` | Deterministische Musterwahl (`ChoosePattern`) und Aufbau eines `ChunkLayout` je Muster; cacht Meshes und Kollisionsformen |
| `ChunkLayout` / `PlatformSpec` / `CrateSpec` | Reine Daten eines Chunks (Plattformen, Kisten, Anker, Ein-/Ausgang) plus Geometrie-Helfer `WalkableGap`, `SupportsPoint` |
| `Chunk` | Node3D eines Abschnitts mit allen Kindern, `WorldBounds`, `RestockSupply()`, `ApplyOriginShift()` |
| `PatternType` | Enum der 12 Muster; `RequiredItems()` nennt die Items, die ein Item-Chunk akzeptiert |
| `PlatformShape` / `ShapeMeshFactory` | Enum der 11 Formen; baut `ArrayMesh`es mit flachen Normalen und UV2-Kantenvertrag |
| `MovingPlatform`, `RotatingPlatform` | `AnimatableBody3D` für pendelnde bzw. drehende Plattformen |
| `Checkpoint`, `GrappleAnchor` | Trigger auf dem Pad bzw. Ziel für den Enterhaken |
| `FrustumUtil` | AABB-gegen-Frustum-Test für den Despawn |
| `MaterialLibrary` / `MaterialNames` | Lädt `art/materials/*.tres`, `GetShapeVariant` für Formen, Magenta-Fallback |
| `Backdrop`, `BackdropMeshes` | Hintergrund, folgt der Kamera, schreibt nur Shader-Uniforms pro Frame |

### Spieler und Items

| Klasse | Aufgabe |
|---|---|
| `PlayerController` | Bewegung, Sprung (Coyote-Time, Sprungpuffer, variable Höhe), Angriff, Item-Einsatz, Enterhaken- und Flugphysik, Checkpoints, Respawn; implementiert `IPlayerAnimState` |
| `ThirdPersonCamera` | Maus-Kamera mit SpringArm gegen Durchclippen |
| `IPlayerAnimState` | Eingefrorenes Interface: Lesezugriff auf den Spielerzustand für die Animation |
| `Inventory` | Vier unbegrenzte Slots, Auswahl |
| `Crate`, `Pickup` | Zerstörbare Kiste mit Drop-Tabelle; schwebendes Item zum Einsammeln |
| `BridgeItem`, `TrampolineItem`, `GrappleHook`, `HelicopterItem` | Statische `TryUse(PlayerController, out string failReason)` je Item |
| `Bridge`, `Trampoline`, `HelicopterRotor` | Platzierte Objekte bzw. Rotor-Visual |
| `Placeables` | Verwaltet platzierte Objekte (max. 3) und ihre Lebensdauer |

### Charaktere, Audio, UI

| Klasse | Aufgabe |
|---|---|
| `CharacterDefinition`, `CharacterCatalog` | Daten der vier Charaktere, Default `superhero` |
| `CharacterModelFactory` | Baut das prozedurale Modell in die Pivot-Hierarchie |
| `CharacterRig` | Prozedurale Animation aus `IPlayerAnimState`; Vorschaumodus ohne Spieler |
| `CharacterMaterials` | Lädt `art/characters/*.tres` oder baut das Material im Code |
| `AudioManager`, `AudioEventBridge`, `Sfx` | siehe [Audio](./audio) |
| `HUD`, `PauseMenu`, `MainMenu`, `CharacterSelect` | Oberflächen |
| `GameManager`, `SaveSystem` | Zustand und Persistenz (`user://highscore.json`, `user://settings.json`, max. 4 KB, atomar geschrieben, robust gegen manipulierte Dateien) |

## Signale

### `PlayerController`

| Signal | Argumente | Wann |
|---|---|---|
| `Jumped` | – | bei jedem Sprung |
| `Landed` | – | bei jeder Landung |
| `Attacked` | – | bei jedem Schlag, auch ohne Treffer |
| `CrateHit` | `Crate crate` | Schlag trifft eine Kiste |
| `ItemUsed` | `int itemType` | Item erfolgreich eingesetzt |
| `ItemUseFailed` | `string reason` | Einsatz nicht möglich (Text für das HUD) |
| `GrappleStarted` / `GrappleEnded` | – | Enterhaken beginnt / endet |
| `FlightStarted` / `FlightEnded` | – | Helikopterflug beginnt / endet |
| `Bounced` | – | Trampolin-Impuls |
| `Respawned` | – | Respawn, auch beim ersten Spawn |
| `CheckpointReached` | `int chunkIndex` | neuer Checkpoint aktiv |

### Weitere

| Quelle | Signal | Argumente |
|---|---|---|
| `Inventory` | `InventoryChanged` | `int bridge, int trampoline, int grapple, int helicopter` |
| `Inventory` | `SelectionChanged` | `int itemType` |
| `Inventory` | `ItemAdded` | `int itemType, int newCount` |
| `Crate` | `Broken` | `int droppedItemType` |
| `WorldGenerator` | `ChunkSpawned` / `ChunkDespawned` | `int index` |
| `WorldGenerator` | `OriginShifted` | `Vector3 offset` |
| `GameManager` | `ScoreChanged` | `int score, int distance` |
| `GameManager` | `HighscoreChanged` | `int highscore` |
| `GameManager` | `PlayerRespawned` | – |
| `GameManager` | `CharacterChanged` | `string id` |

**Wer hört zu?** Das `HUD` verbindet sich mit `ScoreChanged`, `HighscoreChanged`, `ItemUseFailed`, `Respawned`,
`InventoryChanged` und `SelectionChanged`. Die `AudioEventBridge` verbindet sich **per Signalname als String** (nur wenn
`HasSignal` zutrifft) mit den Spieler-Signalen und `Inventory.ItemAdded` – so bleibt das Audio-Modul vom Gameplay-Code
entkoppelt. Der `WorldGenerator` hört auf `Respawned`, um Versorgungskisten aufzufüllen.

## Physik-Engine

`project.godot` setzt `physics/3d/physics_engine="Jolt Physics"` explizit. Jolt behandelt den `CharacterBody3D` an
Kanten von Zylinder- und Konvex-Kollisionen (Scheiben, Säulen, Vielecke) stabiler als GodotPhysics3D.

## Physik-Layer

| Bit | Wert | Layer |
|---|---|---|
| 1 | 1 | Welt (Plattformen) |
| 2 | 2 | Spieler |
| 3 | 4 | Kisten |
| 4 | 8 | Pickups |
| 5 | 16 | Trigger (Checkpoints) |
| 6 | 32 | Enterhaken-Anker |
| 7 | 64 | Platzierte Objekte (Brücke, Trampolin) |

## Wichtige Konstanten

Alle Spielwerte stehen zentral in `scripts/Game/GameConstants.cs`, zum Beispiel:

| Konstante | Wert |
|---|---|
| `ChunkLength` / `ChunksAhead` / `MaxAliveChunks` | 40 m / 6 / 12 |
| `DespawnDistance` / `OriginShiftThreshold` | 30 m / 1000 m |
| `WalkSpeed` / `SprintSpeed` / `Gravity` / `JumpVelocity` | 7 / 11 m/s / 24 m/s² / 10,5 m/s |
| `MaxSafeGap` / `MaxSafeStep` | 6 m / 2 m |
| `BridgeLength` / `BigGapMax` | 16 m / 13,5 m |
| `HelicopterFlightTime` / `HelicopterMaxRise` | 3,5 s / 14 m |
| `DifficultyDistance` | 1500 m |

## Entwurfsregeln

- **Keine externen Assets:** Meshes prozedural, Audio synthetisch, Icons handgeschriebenes SVG.
- **Keine Allokationen pro Frame** in heißen Pfaden (Rig-Animation, Backdrop, Audio-Bridge, HUD).
- **Headless-fähig:** Kein Code darf ohne Renderer oder Audiogerät Fehler werfen – die Tests laufen mit Dummy-Treibern.
- **Determinismus:** Jeder Chunk hat seinen eigenen RNG (`seed * 7919 + index`).
