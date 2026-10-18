# JumpRun v2 – Schnittstellen-Vertrag (verbindlich)

Gilt für alle v2-Rollen: **GAMEPLAY, CHARACTERS, AUDIO, VISUALS** (parallel), danach **DOCS** und **INTEGRATOR**.
`docs/CONTRACT.md` (v1) gilt weiter, soweit dieser Vertrag nichts anderes sagt. Bei Widerspruch gilt **CONTRACT_V2**.
Pfade, Knoten-, Klassen-, Signal- und Ressourcennamen sind case-sensitive und exakt so umzusetzen.
Akzeptanzkriterien je Rolle: `docs/TASKS_V2.md`.

---

## 0. Grundregeln

1. **Dateibesitz (Abschnitt 1) ist hart.** Jede Rolle schreibt nur in eigene Dateien. Wer eine Änderung in einer fremden
   Datei braucht, trägt sie in `docs/integration-notes/<rolle>.md` ein (`gameplay.md`, `characters.md`, `audio.md`,
   `visuals.md`, `docs.md`), mit Datei, exakter Änderung und Begründung. Der INTEGRATOR setzt sie um.
2. Alle Agents arbeiten **gleichzeitig im selben Verzeichnis**. Kein `git stash/checkout/reset`, kein Löschen fremder
   Dateien, kein Formatieren fremder Dateien. `dotnet build` darf jeder ausführen. Bricht der Build durch eine fremde
   Datei, nicht reparieren, sondern kurz warten bzw. in den Integration-Notes vermerken.
3. **Kompilier-Entkopplung:** Keine Rolle referenziert C#-Typen, die eine *andere* parallele Rolle erst noch anlegt.
   Erlaubt sind nur: bestehender v1-Code, die eingefrorene Datei `scripts/Player/IPlayerAnimState.cs` (PM, schon
   vorhanden) und eigene Typen. Kopplung zur Laufzeit erfolgt über Gruppen, Knotenpfade, Signalnamen als Strings,
   Ressourcenpfade und `ResourceLoader.Exists`-Fallbacks.
4. `.tools/` wird nicht verändert. Keine externen/heruntergeladenen Assets: Meshes prozedural, Audio synthetisch
   (Python-Stdlib), Icons handgeschriebenes SVG.
5. **Renderer:** Alles muss in **Compatibility (OpenGL 3, `run-linux.sh`)** und **Forward+ (Windows)** funktionieren.
   Verboten: SDFGI, VoxelGI, SSR, SSIL, Volumetric Fog, `hint_screen_texture`/`hint_depth_texture`, GPUParticles
   (stattdessen CPUParticles3D), Compute. SSAO darf an bleiben (Compatibility ignoriert es). Ziel 60 fps auf einer
   integrierten AMD-GPU bei 1280×720.
6. Headless-Tests müssen weiter laufen: Kein Code darf ohne Renderer/Audio-Gerät (Dummy-Treiber) Fehler werfen.
7. Namespaces nach Ordner: `JumpRun.Characters` (`scripts/Characters`), `JumpRun.Audio` (`scripts/Audio`), sonst wie v1.
   Alle Skriptklassen `public partial class`, Dateiname == Klassenname.
8. Keine Allokationen pro Frame in heißen Pfaden (Rig-Animation, Backdrop-Follow, Audio-Bridge, HUD-Update).

---

## 1. Dateibesitz v2

| Pfad | Besitzer |
|---|---|
| `scripts/Player/IPlayerAnimState.cs` | **PM – eingefroren** (nur lesen) |
| `scripts/Player/PlayerController.cs`, `scripts/Player/ThirdPersonCamera.cs` | GAMEPLAY |
| `scripts/Items/**` | GAMEPLAY |
| `scripts/World/**` **außer** `scripts/World/Backdrop*.cs` | GAMEPLAY |
| `scripts/Game/GameConstants.cs` | GAMEPLAY |
| `scripts/UI/HUD.cs`, `scenes/ui/HUD.tscn` | GAMEPLAY |
| `scenes/objects/**`, `scenes/Pickup.tscn`, `scenes/Crate.tscn` | GAMEPLAY |
| `art/ui/icons/helicopter.svg` (neu) | GAMEPLAY |
| `tests/**` (alle bestehenden Tests + neue Gameplay-Tests) | GAMEPLAY |
| `scripts/Characters/**`, `art/characters/**` | CHARACTERS |
| `scenes/Player.tscn` (gesamte Datei; nicht-visuelle Knoten unverändert lassen, siehe 5.1) | CHARACTERS |
| `scenes/ui/CharacterSelect.tscn`, `scripts/UI/CharacterSelect.cs` (neu) | CHARACTERS |
| `scenes/ui/MainMenu.tscn`, `scripts/UI/MainMenu.cs` | CHARACTERS |
| `art/ui/theme.tres` (nur neue Typvariationen ergänzen, bestehende nicht ändern) | CHARACTERS |
| `scripts/Game/GameManager.cs`, `scripts/Game/SaveSystem.cs` (nur die in 5.6 genannten Ergänzungen) | CHARACTERS |
| `tests/CharacterTest.tscn`, `tests/CharacterTest.cs` (neu) | CHARACTERS |
| `tools/**`, `art/audio/**`, `scripts/Audio/**`, `default_bus_layout.tres` | AUDIO |
| `tests/AudioTest.tscn`, `tests/AudioTest.cs` (neu) | AUDIO |
| `art/env/**`, `art/shaders/**`, `art/fx/**` | VISUALS |
| `art/materials/**` – **neue** Dateien aus 7.2 sowie Backdrop-Materialien; bestehende `.tres` dürfen optisch verbessert werden, Name/Typ bleiben | VISUALS |
| `scripts/World/Backdrop*.cs`, `scenes/Backdrop.tscn` | VISUALS |
| `tests/VisualsTest.tscn`, `tests/VisualsTest.cs` (neu, optional) | VISUALS |
| `website/**` (VitePress), `.github/workflows/docs.yml` | DOCS |
| `project.godot`, `scenes/Main.tscn`, `scripts/Game/Main.cs`, `scenes/ui/PauseMenu.tscn`, `scripts/UI/PauseMenu.cs`, `docs/GDD.md`, `README.md`, `.gitignore`, `.gitattributes`, Git-Repository | INTEGRATOR |
| `JumpRun.csproj` | INTEGRATOR (Builder: Wünsche in Integration-Notes) |
| `docs/integration-notes/<rolle>.md` | jeweilige Rolle |

Hinweis `art/ui/icons/`: bestehende Icons bleiben unverändert; neue Icons nur vom genannten Besitzer.

---

## 2. Konstanten (`scripts/Game/GameConstants.cs`, GAMEPLAY)

Neu bzw. geändert (alle `public const`):

| Name | Typ | Wert | Bedeutung |
|---|---|---|---|
| `MaxItemCharges` | – | **entfernt** | kein Limit mehr pro Item |
| `InventoryHardCap` | int | `999_999` | reiner Überlaufschutz (Sättigung), kein Gameplay-Limit |
| `ItemSlotCount` | int | `4` | Anzahl Item-Slots |
| `HelicopterFlightTime` | float | `3.5f` | Flugdauer pro Ladung (s) |
| `HelicopterHorizontalSpeed` | float | `9f` | m/s |
| `HelicopterAccel` | float | `30f` | m/s² horizontal |
| `HelicopterAscendSpeed` | float | `6f` | m/s bei gehaltenem `jump` |
| `HelicopterSinkSpeed` | float | `1.2f` | m/s Sinken ohne `jump` |
| `HelicopterVerticalAccel` | float | `20f` | m/s² Annäherung an Ziel-vy |
| `HelicopterMaxRise` | float | `14f` | max. Höhe über Start-Y des Flugs |
| `RotatingMaxAngularSpeed` | float | `0.9f` | rad/s, Obergrenze für drehende Plattformen |
| `MinWalkableInnerRadius` | float | `1.2f` | min. Innenradius begehbarer Rund-/Polygonplattformen (m) |
| `MaxRampSlopeDeg` | float | `25f` | max. Rampensteigung |

Alle übrigen v1-Konstanten bleiben (insb. `MaxPlaceables = 3`, `MaxSafeGap = 6.0`, `MaxSafeStep = 2.0`).

---

## 3. Items (GAMEPLAY)

### 3.1 `ItemType` (Reihenfolge verbindlich)
```csharp
public enum ItemType { None = 0, Bridge = 1, Trampoline = 2, GrappleHook = 3, Helicopter = 4 }
```
Slot-Index = `(int)type - 1` (0..3). `Inventory.SelectNext` zyklisch über 4 Slots.

### 3.2 `Inventory : Node` (unbegrenzt)
- `int GetCount(ItemType t)`, `bool Add(ItemType t, int amount = 1)` → **immer true** für gültiges `t` und `amount > 0`
  (sättigt bei `InventoryHardCap`), `bool Consume(ItemType t)`, `ItemType Selected { get; }`, `void Select(ItemType t)`,
  `void SelectNext(int dir)`, `void Clear()`.
- Signale:
  - `InventoryChanged(int bridge, int trampoline, int grapple, int helicopter)` (**4 Argumente**, ersetzt v1-Signatur)
  - `SelectionChanged(int itemType)`
  - **neu** `ItemAdded(int itemType, int newCount)` – nach jedem erfolgreichen `Add`.
- `Pickup` wird damit immer eingesammelt (die Retry-Logik für volles Inventar darf entfallen).

### 3.3 Helikopter (`ItemType.Helicopter`)
- Aktivierung: Taste **4** (InputMap `item_4`, physical keycode 52) wählt und setzt sofort ein (wie 1/2/3 in
  `PlayerController.ActivateItem`), oder `use_item` bei ausgewähltem Helikopter. Am Boden **und** in der Luft erlaubt.
- Einstieg: `static bool HelicopterItem.TryUse(PlayerController p, out string failReason)` in
  `scripts/Items/HelicopterItem.cs`. Erfolgreich → 1 Ladung verbraucht, `p.StartFlight()`.
- Läuft gerade ein Enterhaken, wird er mit `StopGrapple(true)` beendet und der Flug startet.
- Erneutes Drücken von 4 bzw. `use_item` während des Flugs **beendet** den Flug (ohne Verbrauch, ohne Fehlerhinweis).
- Flugphysik (in `PlayerController`, eigener Zweig `ProcessFlight(dt)`): keine Schwerkraft; Ziel-vy = `+HelicopterAscendSpeed`
  bei gehaltenem `jump`, sonst `-HelicopterSinkSpeed`; über `startY + HelicopterMaxRise` wird vy auf ≤ 0 begrenzt;
  horizontal kamerarelativ mit `HelicopterHorizontalSpeed`/`HelicopterAccel`. Fuel sinkt linear über
  `HelicopterFlightTime`; bei 0 endet der Flug, normale Schwerkraft setzt ein (vy bleibt erhalten, kein Coyote-Sprung).
- Flugende außerdem bei: `Respawn`, `ApplyBounce` (Trampolin), `StartGrapple`.
- Rotor-Visual: Szene `res://scenes/objects/HelicopterRotor.tscn`, Wurzel `HelicopterRotor` (Node3D, Skript
  `scripts/Items/HelicopterRotor.cs`, `public bool Active { get; set; }`, `float SpinRate`). `PlayerController._Ready`
  instanziert sie als Kind **`Player/HelicopterRotor`** an lokaler Position `(0, ModelTopHeight, 0)` (Update bei
  Änderung von `ModelTopHeight`), `visible = false`; sichtbar + drehend während des Flugs. Materialien
  `helicopter_rotor`, `helicopter_hub` (VISUALS). Fehlt die Szene → kein Visual, Flug funktioniert trotzdem.
- Fehlertexte (deutsch): bestehende v1-Texte; neu keiner nötig.
- `PatternTypeExtensions.RequiredItems`: `BigGap → {Bridge, GrappleHook, Helicopter}`,
  `TallWall → {Trampoline, Helicopter}` (Helikopter ist überall eine gültige Alternative). Versorgungskisten
  (`ForcedDrop`) bleiben wie v1 (Brücke/Enterhaken bzw. Trampolin) – die Garantie hängt nicht am Helikopter.

### 3.4 Drop-Tabelle `Crate.RollDrop(Random)`
| Ergebnis | Gewicht (von 100) |
|---|---|
| Bridge | 27 |
| Trampoline | 27 |
| GrappleHook | 22 |
| Helicopter | 12 |
| None | 12 |

Start-Chunk (Index 0) darf zusätzlich eine dritte Tutorial-Kiste mit `ForcedDrop = Helicopter` bei `(0, 0.5, -26)` erhalten.
Pickup-Material für den Helikopter: `pickup_helicopter`.

---

## 4. Plattform-Formen (GAMEPLAY baut, VISUALS färbt)

### 4.1 `PlatformShape` (`scripts/World/PlatformShape.cs`, Reihenfolge verbindlich)
```csharp
public enum PlatformShape
{
    Box = 0,       // v1 (BoxMesh + BoxShape3D), alle Materialien wie v1
    Square = 1,    // Box mit Size.X == Size.Z
    Diamond = 2,   // Quadrat um 45° gedreht (Size.X == Size.Z)
    Cylinder = 3,  // rund, hoher Körper (Size.Y >= 2), Säule
    Disc = 4,      // rund, flach (Size.Y <= 0.6)
    Triangle = 5,  // aufrechtes Dreiecksprisma
    Pentagon = 6,
    Hexagon = 7,
    Octagon = 8,
    Ramp = 9,      // Keil, steigt in -Z (Laufrichtung)
    Cross = 10,    // Plus-Form aus zwei Balken
}
```

### 4.2 `PlatformSpec`-Erweiterung (`scripts/World/ChunkLayout.cs`)
Neue Felder (Defaults erhalten v1-Verhalten exakt):
- `PlatformShape Shape = PlatformShape.Box`
- `float RotationY` (rad, um die Plattformmitte; nur erlaubt, wenn `Size.X == Size.Z` oder Shape == Cross)
- `float SpinSpeed` (rad/s; ≠ 0 → `RotatingPlatform`; `|SpinSpeed| ≤ RotatingMaxAngularSpeed`; nur für Disc/Cylinder/
  Polygone/Square/Diamond/Cross; nie gleichzeitig mit `MoveOffset`)
- `float Rise` (nur Ramp: Höhenunterschied der Oberfläche; `Size.Y = Rise + 0.6`)
- Abgeleitete Properties: `bool IsRotating`, `float TopY` (höchster Punkt der Oberfläche, bei Ramp am −Z-Ende),
  `float EntryTopY` (Oberfläche am +Z-Ende; = `TopY` außer bei Ramp), `float InnerRadius` (Innenkreis der Grundfläche:
  rund = Size.X/2, n-Eck = R·cos(π/n) mit R = Size.X/2, Box/Square = min(Size.X, Size.Z)/2),
  `bool IsRoundOrPolygon`.
- `Size` bleibt die **achsenparallele Bounding-Box** (`ComputeLocalBounds` bleibt gültig). Für runde/polygonale Formen
  gilt `Size.X == Size.Z == 2R` (Umkreisdurchmesser), damit jede `RotationY`/Drehung in der AABB bleibt.
- Neue statische Geometrie-Helfer in `ChunkLayout` (von Generator **und** Tests benutzt):
  - `static float WalkableGap(PlatformSpec a, PlatformSpec b)` – konservativer horizontaler Abstand der begehbaren
    Flächen: Box/Square/Ramp/Cross = Rechteck(e) (Diamond = gedrehtes Quadrat → Innenkreis), rund/Polygon/drehend =
    Kreis mit `InnerRadius`. Nie kleiner als der echte Abstand.
  - `static bool SupportsPoint(PlatformSpec p, Vector3 localPos, float margin)` – Punkt (XZ) liegt mit Rand `margin`
    auf der begehbaren Fläche (für Kisten).

### 4.3 Erzeugung (`ChunkBuilder`)
- Unit-Meshes je (Shape, Material) gecacht; Nicht-Box-Formen werden von **`ShapeMeshFactory`**
  (`scripts/World/ShapeMeshFactory.cs`, `public static class`) als `ArrayMesh` gebaut: Mittelpunkt im Ursprung,
  X/Z ∈ [−0.5, 0.5], Y ∈ [−0.5, 0.5] (wie BoxMesh, Node-`Scale = Size`), **flache Normalen pro Fläche**, erste Ecke eines
  n-Ecks zeigt nach −Z. Ramp darf pro Steigungsverhältnis (gerundet 0,05) gecacht werden. Cache leeren in
  `ChunkBuilder.ClearSceneCache()`.
- **Mesh-Attribut-Vertrag für VISUALS** (Outline unabhängig von Skalierung):
  - `UV` = planar in Mesh-Einheiten (Deckel: XZ, Seiten: (Umfangsparameter, Höhe)).
  - `UV2` ∈ [0,1]²: Flächenränder liegen exakt bei `UV2.x ∈ {0,1}` oder `UV2.y ∈ {0,1}`; Deckel/Böden als Fächer:
    Randecken `UV2 = (0.5, 0.0)`, Mittelpunkt `UV2 = (0.5, 1.0)` (→ nur der Außenrand ist Kante).
    Seitenflächen: Quad mit `UV2` (0,0)…(1,1). Shader-Outline = `min(UV2.x, 1-UV2.x, UV2.y, 1-UV2.y) / fwidth(...)`.
  - `COLOR` nicht benutzt.
- Kollision exakt zur Darstellung (±1 cm): Box/Square/Diamond → `BoxShape3D` (Diamond: gedrehte CollisionShape);
  Cylinder/Disc → `CylinderShape3D`; Triangle/Pentagon/Hexagon/Octagon/Ramp → `ConvexPolygonShape3D` (Punkte =
  skalierte Mesh-Ecken, gecacht je (Shape, gerundete Size)); Cross → zwei `BoxShape3D`. Keine Concave/Trimesh-Shapes.
- Bewegte Formen: `MovingPlatform` (v1) für `MoveOffset`; **neu** `RotatingPlatform : AnimatableBody3D`
  (`scripts/World/RotatingPlatform.cs`, `[Export] float AngularSpeed`, `sync_to_physics = true`, dreht um lokales Y).
  `PlayerController.IsOnMovingPlatform()` liefert auch für `RotatingPlatform` true (Floating-Origin-Schutz).
- Materialwahl: Box-Formen → `MaterialLibrary.Get(name)` wie v1; alle anderen Formen →
  **`MaterialLibrary.GetShapeVariant(name)`** = lädt `res://art/materials/{name}_shape.tres`, Fallback ohne Warnung auf
  `Get(name)`.
- Deterministik: `ChoosePattern` bleibt erster Verbraucher des Chunk-RNG; Formwahl erst danach.

### 4.4 Neue Muster (`PatternType`, **ans Ende angehängt**)
```csharp
Start, GapJump, Stairs, ZigZag, MovingPlatforms, NarrowBeams, HeightJump, BigGap, TallWall,
Pillars, SpinningDiscs, ShapeMix
```
| Muster | Inhalt | Material |
|---|---|---|
| `Pillars` | 5–8 Säulen (`Cylinder`/`Hexagon`/`Octagon`, Size.Y 3–8 m, Durchmesser 3,2 → 2,6), Höhenversatz ≤ MaxSafeStep | `platform_pillars` |
| `SpinningDiscs` | 3–5 drehende `Disc`/`Hexagon`/`Triangle`-Plattformen (`SpinSpeed` 0,4 → 0,9 rad/s, Vorzeichen zufällig), ab Index ≥ 4 | `platform_spin` |
| `ShapeMix` | 4–7 gemischte Formen aller Typen inkl. `Ramp` (≤ 25°) und `Cross` | `platform_shapes` |

Gewichte in `ChoosePattern` (Nicht-Item-Muster): `Pillars 1 + 1·d`, `SpinningDiscs 0.5 + 1.5·d` (0 bei Index < 4),
`ShapeMix 1.5`. Bestehende Regeln (Item-Chunks, Streak ≤ 2, Index 0–2 ohne Item) bleiben. `MaterialNames.ForPattern`
bekommt die drei neuen Einträge.

Bestehende Muster werden variiert (nur nach dem Musterwürfeln gewürfelt):
`GapJump` (Box/Square/Disc/Hexagon/Octagon/Triangle), `Stairs` (Box/Square/Cylinder/Ramp), `ZigZag` (Disc/Hexagon/
Diamond/Square), `HeightJump` (Cylinder/Hexagon/Octagon/Box), `MovingPlatforms` (Box/Disc/Hexagon).
**Unverändert reine Boxen:** `Start`, `NarrowBeams`, Checkpoint-Pad, Landing (`AddLanding`), `BigGap`, `TallWall`
(Plattform-Reihenfolge `Platforms[1..3]` bleibt, die Item-Lösbarkeitstests hängen daran).

### 4.5 Garantien (bleiben, mit Formgeometrie)
- Für alle Nicht-Item-Muster: `WalkableGap` ≤ erreichbare Sprungweite − 0,8 m und Stufe (`EntryTopY(next) − TopY(prev)`)
  ≤ `MaxSafeStep`, auch im ungünstigsten Fall bewegter Plattformen; drehende Plattformen über `InnerRadius`
  (drehinvariant).
- `InnerRadius ≥ MinWalkableInnerRadius` für jede begehbare Rund-/Polygonform.
- Kisten nur auf statischen, nicht drehenden Plattformen, `SupportsPoint(p, pos, 0.3f)` und `pos.Y − 0.5 == TopY`
  (keine Kisten auf Ramp).
- Alle v1-Garantien zu Item-Chunks, Vorgänger-Kiste, `RestockSupply`.

---

## 5. Spieler, Animation, Charaktere

### 5.1 `scenes/Player.tscn` (CHARACTERS)
```
Player (CharacterBody3D, PlayerController.cs, Gruppe player, layer 2, mask 69)   – unverändert
├── CollisionShape3D (CapsuleShape3D r=0.4 h=1.8)                                – unverändert (alle Charaktere)
├── Visual (Node3D)                         – PlayerController dreht nur dessen Yaw (wie v1)
│   └── Rig (Node3D, res://scripts/Characters/CharacterRig.cs)   – Visual/Body und Visual/Visor entfallen
├── Inventory (Node, Inventory.cs)                                               – unverändert
├── GrappleRope (MeshInstance3D …)                                               – unverändert
└── CameraRig (ThirdPersonCamera) → Pitch → SpringArm3D → Camera3D               – Camera3D.far = 1500 (für Backdrop)
```
`Player/HelicopterRotor` wird zur Laufzeit von GAMEPLAY angelegt (nicht in der .tscn).

### 5.2 `IPlayerAnimState` (eingefroren, `scripts/Player/IPlayerAnimState.cs`)
Bereits vorhanden – siehe Datei. Mitglieder: `Velocity`, `IsOnFloor()`, `IsGrappling`, `IsFlying`, `MoveSpeedRatio`,
`HorizontalSpeed`, `Facing`, `GrappleTargetPosition`, `FlightFuelRatio`, `AttackCount`, `JumpCount`, `LandCount`,
`AnimMode` (`PlayerAnimMode { Idle, Run, Jump, Fall, Grapple, Fly }`), `ModelTopHeight { get; set; }` (Default 2.0).
- GAMEPLAY: `public partial class PlayerController : CharacterBody3D, IPlayerAnimState` und implementiert alles exakt
  nach den Doc-Kommentaren (`AnimMode`-Priorität: Grapple > Fly > Boden (Run/Idle) > Luft (Jump/Fall)).
- CHARACTERS: `CharacterRig` holt `GetParent()?.GetParent() as IPlayerAnimState` in `_Ready`; null → Preview/Idle.
  Setzt nach dem Modellbau `state.ModelTopHeight = <Modellhöhe>`. Sonst nur lesend.

### 5.3 PlayerController – neue öffentliche API (GAMEPLAY)
- Properties: alle aus `IPlayerAnimState`, plus `float FlightTimeLeft { get; }`.
- Methoden: `void StartFlight()`, `void StopFlight()`.
- Signale (Arität verbindlich; AUDIO verbindet per String-Namen):

| Signal | Argumente | Wann |
|---|---|---|
| `Jumped` | – | v1 |
| `Landed` | – | v1 |
| `CrateHit` | `Crate crate` | v1 |
| `Respawned` | – | v1 (auch beim ersten Spawn nach `Reset`) |
| `CheckpointReached` | `int chunkIndex` | v1 (auch für Chunk 0 beim Start) |
| `ItemUseFailed` | `string reason` | v1 |
| `Attacked` | – | **neu**: jeder Schlag (auch ohne Treffer) |
| `ItemUsed` | `int itemType` | **neu**: nach erfolgreichem Einsatz (Brücke/Trampolin gesetzt, Haken geschossen, Flug gestartet) |
| `GrappleStarted` | – | **neu**: in `StartGrapple` |
| `GrappleEnded` | – | **neu**: in `StopGrapple` (nur wenn aktiv war) |
| `FlightStarted` | – | **neu** |
| `FlightEnded` | – | **neu** (Fuel leer, abgebrochen, Respawn, Bounce, Grapple) |
| `Bounced` | – | **neu**: in `ApplyBounce` |

### 5.4 Charakterdaten (CHARACTERS, `scripts/Characters/`)
- `public sealed class CharacterDefinition` (reine Daten): `string Id`, `string DisplayName`, `string Description`,
  `float ModelHeight`, `Color Primary`, `Color Secondary`, `Color Accent` (+ frei weitere).
- `public static class CharacterCatalog`: `const string DefaultId = "superhero"`,
  `IReadOnlyList<CharacterDefinition> All` (Reihenfolge unten), `CharacterDefinition Get(string? id)` (unbekannt →
  Default), `bool IsValid(string? id)`.

| Id | DisplayName | Merkmale (prozedural) |
|---|---|---|
| `bottle` | Flasche | Glas-/PET-Körper (Zylinder + Hals + Deckel), Etikett-Band, Augen, dünne Arme/Beine |
| `trashcan` | Mülleimer | Tonne mit Rillen, Deckel als „Kopf“ (klappt beim Springen), Augen im Spalt, Arme/Beine |
| `superhero` | Superheld | Humanoid, Maske, Emblem, **Umhang** (schwingt mit Tempo) |
| `knight` | Ritter | Rüstung, **Helm** mit Visier + Federbusch, **Schwert** rechts (Schlag-Animation), Schild links |

- `public static class CharacterModelFactory` mit `static void Build(CharacterDefinition def, CharacterRig rig)`.
- Materialien: `art/characters/*.tres` (StandardMaterial3D, kein Textur-Import) oder im Code gecacht. Ressourcen-Limits je
  Charakter: ≤ 24 MeshInstance3D, ≤ 4000 Dreiecke, ≤ 6 Materialien, `cast_shadow` an für Körper, aus für Kleinteile.
- Modell: Füße bei y = 0, Blick nach −Z, Höhe 1,6–2,0 m, Rumpf innerhalb Radius 0,45 (Arme/Accessoires dürfen
  hinausragen). Rein kosmetisch – Kollision/Physik sind für alle gleich.

### 5.5 `CharacterRig : Node3D` (`scripts/Characters/CharacterRig.cs`)
- Exports/Props: `[Export] string CharacterIdOverride = ""` (leer → `GameManager.Instance?.SelectedCharacter` → Default),
  `[Export] bool PreviewMode` (true: kein Player, Idle + gelegentlicher Demo-Lauf), `string CharacterId { get; }`,
  `float ModelHeight { get; }`.
- Methode: `void Build(string characterId)` (baut Kinder neu; mehrfach aufrufbar, alte Kinder `QueueFree`).
- Knotenhierarchie nach `Build` (Namen verbindlich, für Tests):
  ```
  Rig
  └── Hips (Node3D, Hüfthöhe)
      ├── Body (Node3D)            → Head, ArmL, ArmR (Pivot an Schultern), Accessoires (Cape | Lid | Shield …)
      │   ├── Head (Node3D)
      │   ├── ArmL (Node3D)        (Knight: Shield darunter)
      │   └── ArmR (Node3D)        (Knight: Sword darunter)
      ├── LegL (Node3D, Pivot an Hüfte)
      └── LegR (Node3D)
  ```
  Meshes sind Kinder der Pivots. Pivot-Rotationen sind die einzige Animation (kein AnimationPlayer nötig).
- Animation (in `_Process`, gelesen aus `IPlayerAnimState`, weich geblendet, frame-rate-unabhängig):
  Laufzyklus mit Phase ∝ `HorizontalSpeed` (Schrittlänge ≈ 1,4 m), Amplitude ∝ `MoveSpeedRatio`, Arme gegenphasig;
  Idle-Atmen; `Jump` (Arme hoch, Beine angezogen); `Fall` (Arme seitlich, leichtes Rudern); `Fly` (Beine baumeln,
  Arme nach oben zum Rotor; Superheld: Arm nach vorn); `Grapple` (rechter Arm zeigt zu `GrappleTargetPosition`);
  Schlag bei Änderung von `AttackCount` (ArmR-Schwung 0,25 s, Ritter mit Schwert); Landungs-Stauchung bei
  `LandCount`-Änderung; Umhang/Deckel reagieren auf Tempo bzw. Sprung.

### 5.6 Auswahl + Persistenz (CHARACTERS)
- `GameManager` (nur diese Ergänzungen):
  - `public const string CharacterSelectScenePath = "res://scenes/ui/CharacterSelect.tscn";`
  - `public string SelectedCharacter { get; set; }` (Setter validiert über `CharacterCatalog.IsValid`, sonst Default;
    markiert Settings dirty)
  - `public float MusicVolume { get; set; }` und `public float SfxVolume { get; set; }` (linear 0..1, geklemmt,
    Default 0.7 / 0.8, markiert Settings dirty) – nur Daten; Anbindung an AudioManager macht der INTEGRATOR.
  - Signal `CharacterChanged(string id)`.
  - In `EnsureInputMap`: `AddKey("item_4", Key.Key4);`
- `SaveSystem`/`SettingsData`: neue Felder `Character` (JSON `"character"`), `MusicVolume` (`"music_volume"`),
  `SfxVolume` (`"sfx_volume"`). `version` bleibt **1**; fehlende Felder → Defaults; `character` nur akzeptieren, wenn
  String, ≤ 16 Zeichen, ASCII `[a-z]` und `IsValid`; Volumes nur endliche Zahlen, geklemmt. Alle v1-Regeln (4 KB,
  atomar, feste Pfade) bleiben; `TesterSecurityTest` muss weiter bestehen.
- `MainMenu.tscn`: bestehende Knoten bleiben (`%StartButton`, `%QuitButton`, `%HighscoreLabel`, `%SeedEdit`), neu
  `%CharacterButton` (Button „Charakter: <Name>“ oder „Charakter wählen“) und optional `%CharacterLabel`.
  `%CharacterButton` → `ChangeSceneToFile(CharacterSelectScenePath)`. Seed-Eingabe darf beim Wechsel verloren gehen.
- `CharacterSelect.tscn` (Wurzel `CharacterSelect`, Control, Skript `res://scripts/UI/CharacterSelect.cs`, Theme
  `art/ui/theme.tres`), Unique Names:
  `%PrevButton`, `%NextButton`, `%ConfirmButton` („Auswählen“), `%BackButton` („Zurück“), `%CharacterName` (Label),
  `%CharacterDescription` (Label), `%PreviewContainer` (SubViewportContainer, `stretch = true`) →
  `SubViewport` (`own_world_3d = true`, transparenter oder Verlauf-Hintergrund) mit `Camera3D`, `DirectionalLight3D`,
  `Turntable (Node3D, dreht 0,6 rad/s)` → `Rig (CharacterRig, PreviewMode = true)`.
  Tasten: A/←, D/→ wechseln, Enter bestätigt, Esc zurück. Bestätigen → `SelectedCharacter`, `SaveAll()`, zurück ins
  Hauptmenü. Optional Mini-Leiste mit allen 4 Charakteren.
- Theme: neue Typvariationen nur additiv (z. B. `CharacterCard`, `CharacterCardSelected`).

---

## 6. Audio (AUDIO)

### 6.1 Generator `tools/gen_audio.py`
- Nur Python-Stdlib (`wave`, `struct`, `math`, `random` mit festem Seed), deterministisch, Aufruf
  `python3 tools/gen_audio.py` aus dem Projektroot, schreibt nach `art/audio/`. Mono, 22 050 Hz, 16 bit PCM.
- Musik `art/audio/music_main.wav`: freundlicher Chiptune/Lo-Fi-Loop, 60–90 s, nahtlos loopbar (Länge = ganze Takte,
  kein Knacken am Loop-Punkt, Fade-freier Übergang), Spitzenpegel ≤ −1 dBFS.
- SFX (je ≤ 2 s, außer Loops): `jump`, `land`, `swing`, `crate_break`, `pickup`, `bridge_place`, `trampoline_place`,
  `trampoline_bounce`, `grapple_shoot`, `grapple_attach`, `helicopter_start`, `helicopter_loop` (nahtlos, 1–2 s),
  `respawn`, `checkpoint`, `item_fail`, `ui_click`, `ui_hover` (optional).
- Gesamtgröße `art/audio/` ≤ 6 MB. Die erzeugten `.wav` werden mit eingecheckt (Godot importiert sie bei `--import`).

### 6.2 Busse `res://default_bus_layout.tres`
`Master` → `Music` (send Master), `SFX` (send Master). Optional leichter Limiter auf Master.

### 6.3 `AudioManager : Node` (`scripts/Audio/AudioManager.cs`, Autoload **`AudioManager`**, `/root/AudioManager`)
- `public static AudioManager? Instance { get; }`; `ProcessMode = Always` (Musik läuft im Pause-Menü weiter).
- `void PlayMusic(string name = "music_main", float fadeSeconds = 1f)`, `void StopMusic(float fadeSeconds = 0.5f)`;
  startet in `_Ready` automatisch `music_main` (läuft szenenübergreifend weiter).
- `void PlaySfx(string name, Vector3? position = null, float volumeDb = 0f, float pitchJitter = 0.05f)` – mit
  `position` über einen Pool von 8 `AudioStreamPlayer3D`, sonst Pool von 12 `AudioStreamPlayer` (Bus `SFX`);
  Voice-Stealing des ältesten; unbekannter Name → einmalige Warnung, kein Fehler.
- `void SetLoop(string name, bool on)` – nicht-positionale Dauerschleife (z. B. `helicopter_loop`), idempotent.
- Lautstärke (linear 0..1, setzt Bus-dB, 0 → stumm): `float MasterVolume`, `float MusicVolume`, `float SfxVolume`
  (get/set). Keine eigene Persistenz.
- Laden: `res://art/audio/{name}.wav` mit `ResourceLoader.Exists`-Prüfung, Cache. Loop zur Laufzeit setzen
  (`AudioStreamWav.LoopMode = Forward`, `LoopBegin = 0`, `LoopEnd = (int)(stream.GetLength() * stream.MixRate)`) – gilt
  unabhängig von der Import-Kompression.
- Klick-Sounds: verbindet sich mit `GetTree().NodeAdded` und hängt an jeden `BaseButton` `Pressed → ui_click`.
- Namen als Konstanten in `public static class Sfx` (`scripts/Audio/Sfx.cs`), z. B. `Sfx.Jump = "jump"`.

### 6.4 `AudioEventBridge : Node` (`scripts/Audio/AudioEventBridge.cs`)
Wird vom INTEGRATOR als `Main/AudioEventBridge` in `Main.tscn` eingehängt. Findet deferred den Spieler (Gruppe `player`)
und dessen Kind `Inventory`, verbindet **per String-Namen** (nur wenn `HasSignal`, sonst still überspringen) mit
den Signalen aus 5.3 und `Inventory.ItemAdded`, trennt in `_ExitTree`. Mapping:

| Ereignis | Sound |
|---|---|
| `Jumped` | `jump` |
| `Landed` | `land` (gedrosselt: max. 1× pro 0,15 s) |
| `Attacked` | `swing` |
| `CrateHit` | `crate_break` (positional an der Kiste) |
| `Inventory.ItemAdded` | `pickup` |
| `ItemUsed(1)` / `(2)` / `(3)` / `(4)` | `bridge_place` / `trampoline_place` / `grapple_shoot` / `helicopter_start` |
| `GrappleEnded` nach Ankunft | `grapple_attach` (vereinfachbar: bei `GrappleStarted` verzögert 0,1 s) |
| `FlightStarted` / `FlightEnded` | `SetLoop("helicopter_loop", true/false)` |
| `Bounced` | `trampoline_bounce` |
| `Respawned` | `respawn` (die ersten 0,5 s nach Szenenstart ignorieren) |
| `CheckpointReached` | `checkpoint` (Chunk 0 bzw. erste 0,5 s ignorieren) |
| `ItemUseFailed` | `item_fail` |

Alle Aufrufe über `AudioManager.Instance?.…` (null-sicher, damit Tests ohne Autoload laufen).

---

## 7. Grafik (VISUALS)

### 7.1 Hintergrund `res://scenes/Backdrop.tscn`
- Wurzel **`Backdrop`** (Node3D, Skript `res://scripts/World/Backdrop.cs`), vom INTEGRATOR als **`Main/Backdrop`**
  instanziert (Kind von `Main`, **nicht** von `WorldRoot` → wird nicht vom Floating Origin verschoben).
- Folgt in `_Process` der aktiven Kamera (`GetViewport().GetCamera3D()`) in X/Z (Y fest bzw. gedämpft). Parallaxe/
  Wiederholung weltstabiler Elemente über die absolute Z-Koordinate `absZ = local.Z − GameManager.Instance.OriginOffsetZ`
  (bleibt beim Origin-Shift stetig). Optional zusätzlich `WorldGenerator.OriginShifted` per String-Signal.
- Inhalte (Vorschlag, Budget verbindlich): Himmel-Shader (`art/shaders/sky.gdshader`, `shader_type sky`: Verlauf,
  Sonnenscheibe + Halo aus `LIGHT0_DIRECTION`, weiche Wolken), 2–3 Ringe ferner Berg-/Insel-Silhouetten
  (500–1200 m, Höhenverlauf + Dunst im Shader), schwebende Inseln (250–600 m), Wolkenbänke unterhalb der Strecke,
  wenige Schwebe-Partikel (CPUParticles3D ≤ 64) nahe der Kamera.
- Budget Backdrop: ≤ 16 Draw Calls, ≤ 60 000 Dreiecke, kein Schattenwurf, keine Transparenz-Overdraw-Flächen über
  > 30 % des Bildschirms, keine Textur-Dateien.
- Ferne Objekte liegen hinter dem Tiefennebel → Backdrop-Materialien nutzen `render_mode fog_disabled` (bzw.
  `unshaded`) und berechnen ihren eigenen Dunst hin zur Horizontfarbe; Farben passen zum Nebel.
- Spawn-Verdeckung bleibt: neue Chunks (≈ 240 m voraus) müssen weiterhin aus dem Nebel „einblenden“
  (Nebelende ≤ 220 m für Welt-Geometrie). `Camera3D.far = 1500` setzt CHARACTERS in `Player.tscn`.
- Sky-Performance: Radiance nicht pro Frame neu berechnen (Sky `process_mode` passend, `radiance_size` ≤ 64); wenn der
  Sky-Shader `TIME` nutzt, Ambient/Reflections nicht aus dem Sky (Color-Ambient), sonst Wolken statisch.

### 7.2 Neue Materialien `res://art/materials/` (Namen verbindlich; Konstanten legt GAMEPLAY in `MaterialNames` an)
| Datei | Verwendung |
|---|---|
| `platform_pillars.tres`, `platform_spin.tres`, `platform_shapes.tres` | Box-Variante der neuen Muster (bestehender `platform.gdshader`) |
| `platform_gap_shape.tres`, `platform_stairs_shape.tres`, `platform_zigzag_shape.tres`, `platform_moving_shape.tres`, `platform_height_shape.tres`, `platform_pillars_shape.tres`, `platform_spin_shape.tres`, `platform_shapes_shape.tres` | Nicht-Box-Formen (neuer Shader `art/shaders/platform_shape.gdshader`, nutzt den UV2-Vertrag aus 4.3; Deckfläche = Normale·Up > 0.5, auch für Rampen) |
| `pickup_helicopter.tres` | Pickup (Signaturfarbe Helikopter, z. B. Violett `#9B6BFF` – kein Rot/Orange/Magenta) |
| `helicopter_rotor.tres`, `helicopter_hub.tres` | Rotor-Visual am Spieler |

Farbregeln aus `art/STYLE.md` bleiben (Rot/Orange nur für Item-Pflicht, Magenta = Fallback). `art/STYLE.md` darf VISUALS
ergänzen. Shape-Materialien müssen auf allen Formen und Skalierungen saubere, pixelkonstante Kanten zeigen.

### 7.3 Licht/Umgebung
`art/env/world_environment.tres` (Sky = ShaderMaterial aus `sky.gdshader`, Nebel, Tonemap, Glow) und
`art/env/Lighting.tscn` (`Lighting/Sun` wie v1, Schatten an, `directional_shadow_max_distance` ≤ 150) bleiben die
Einstiegspunkte (Pfade unverändert). Optional ein schattenloses Fülllicht. Keine weiteren schattenwerfenden Lichter.

---

## 8. HUD (GAMEPLAY, `scenes/ui/HUD.tscn` + `scripts/UI/HUD.cs`)
- Vier Slots: zusätzlich `%ItemSlot4`, `%ItemSlot4Count`, `%ItemSlot4Icon` (Icon `res://art/ui/icons/helicopter.svg`),
  Tastenlabel „[4] Helikopter“. Zähler-Format `x{n}` ohne Obergrenze (z. B. „x12“, „x1234“), Slot-Breite wächst mit.
- Tankanzeige: `%FuelPanel` (Control, nur während `IsFlying` sichtbar) mit `%FuelBar` (ProgressBar, 0..1 =
  `FlightFuelRatio`).
- `ControlsHint` auf „1-4 / Mausrad Item wählen“ aktualisieren; Hinweis „Helikopter: Leertaste halten = steigen“.
- Auswahl-Hervorhebung wie v1 (`ItemSlot` / `ItemSlotSelected`).

---

## 9. Dokumentation (DOCS, `website/`)
- VitePress im Ordner **`website/`** (nicht `docs/`, dort liegen die Projekt-Dokumente). **`website/.gdignore`
  (leere Datei) ist Pflicht**, damit Godot `node_modules` nicht importiert.
- `website/package.json`: `"private": true`, `"type": "module"`, DevDependency `vitepress` mit **exakter Version**
  (z. B. `1.6.3`, kein `^`), Skripte `docs:dev`, `docs:build`, `docs:preview`. `package-lock.json` einchecken.
- `website/.vitepress/config.mts`: `lang: 'de-DE'`, `title: 'JumpRun'`, Navigation + Sidebar, lokale Suche
  (`search.provider = 'local'`), `base` per Umgebungsvariable `DOCS_BASE` (Default `/`) für GitHub Pages.
- Seiten (deutsch): `index.md` (Hero), `guide/installation.md`, `guide/steuerung.md`, `spiel/items.md` (inkl.
  Helikopter, unbegrenztes Inventar), `spiel/charaktere.md`, `spiel/plattformen.md` (alle Formen + Muster),
  `spiel/welt.md` (Chunks, Checkpoints, Schwierigkeit), `technik/architektur.md` (Szenenbaum, Klassen, Signale),
  `technik/audio.md`, `technik/grafik.md` (Renderer-Regeln), `technik/tests.md`, `entwicklung/mitwirken.md`
  (Rollen, Build, Audio neu generieren, Docs bauen, Git-Workflow).
- Inhalte aus diesem Vertrag/GDD; Diagramme als Mermaid nur, wenn ohne Zusatzplugin darstellbar, sonst ASCII.
- Optional `.github/workflows/docs.yml` (Build + Deploy auf GitHub Pages, `DOCS_BASE=/<repo>/`).
- Akzeptanz: `cd website && npm ci && npm run docs:build` ohne Fehler und ohne tote interne Links.

---

## 10. Integration (INTEGRATOR, nach den Buildern)
1. `project.godot`: Autoloads in dieser Reihenfolge `GameManager`, `AudioManager="*res://scripts/Audio/AudioManager.cs"`;
   Input `item_4` (physical keycode 52); Bus-Layout wird automatisch aus `res://default_bus_layout.tres` geladen.
2. `scenes/Main.tscn`: `Main/Backdrop` (Instanz `scenes/Backdrop.tscn`), `Main/AudioEventBridge` (Node, Skript
   `scripts/Audio/AudioEventBridge.cs`); bestehende Struktur bleibt.
3. Lautstärken: beim Start `GameManager.MusicVolume/SfxVolume` → `AudioManager`; Pause-Menü bekommt
   `%MusicSlider`, `%SfxSlider` (HSlider 0..1, Schritt 0.05) neben `%SensitivitySlider`.
4. Alle `docs/integration-notes/*.md` abarbeiten, `docs/GDD.md` auf v2 aktualisieren, `README.md` (Steuerung 1–4,
  Charaktere, Audio-Generator, Docs-Website), `art/STYLE.md`-Abgleich.
5. Git: `.gitignore` (mind. `.godot/`, `.tools/`, `bin/`, `obj/`, `*.tmp`, `website/node_modules/`,
   `website/.vitepress/cache/`, `website/.vitepress/dist/`, `.vs/`, `*.user`), `.gitattributes` (`* text=auto eol=lf`,
   `*.wav binary`, `*.png binary`, `*.cs text diff=csharp`). `*.uid`- und `*.import`-Dateien werden eingecheckt.
   `git init -b main`, Initial-Commit (der Nutzer pusht selbst; kein Remote anlegen, nicht pushen).
6. Komplettlauf: `dotnet build` (0 Fehler), `--import`, alle `tests/*.tscn` Exit 0, `npm run docs:build`, kurzer
   Sichttest per `run-linux.sh` (falls Display vorhanden).

---

## 11. Test- und Build-Befehle
```bash
G=/home/bichl/git/godot-test/.tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64
cd /home/bichl/git/godot-test
dotnet build                                   # 0 Fehler
$G --headless --path . --import                # nach neuen Ressourcen
for t in tests/*.tscn; do $G --headless --fixed-fps 60 --path . "res://$t" > /dev/null 2>&1; echo "$t -> $?"; done
python3 tools/gen_audio.py                     # Audio neu erzeugen (AUDIO)
(cd website && npm ci && npm run docs:build)   # Doku (DOCS)
```
Neue Testszenen folgen dem v1-Muster: Wurzel-Node mit Skript `JumpRun.Tests.<Name>`, letzte Zeile
`<NAME>_PASS` / `<NAME>_FAIL: <grund>`, Exit-Code 0/1, `GameManager.PersistenceEnabled = false`.
