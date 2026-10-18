# Integration-Notes GAMEPLAY

Status: G1–G6 umgesetzt (unbegrenztes Inventar, Helikopter, `IPlayerAnimState`, Plattform-Formen, HUD, Tests).

## 1. `project.godot` (INTEGRATOR)
Input-Aktion `item_4` (physical keycode 52) ergänzen, analog `item_1..3`:
```ini
item_4={
"deadzone": 0.2,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":-1,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":52,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```
`GameManager.EnsureInputMap` legt `item_4` bereits an (CHARACTERS). `PlayerController` prüft `InputMap.HasAction("item_4")`,
fehlt die Aktion, gibt es keine Fehlermeldung (nur Taste 4 wirkt dann nicht; Mausrad + `use_item` gehen).

## 2. Neue Tests in den Komplettlauf aufnehmen
- `tests/ShapeCollisionTest.tscn` → `SHAPE_COLLISION_TEST_PASS` (Kollision je Form, drehende Disc 5 s).
- `tests/HelicopterTest.tscn` → `HELICOPTER_TEST_PASS` (Flugphysik, Tank, Flugende, Taste 4, Rotor, AnimMode,
  BigGap/TallWall mit Helikopter, Pickup bei großem Bestand).
- `LogicTests` (über `SmokeTest`) enthält neu: `Inventory`, `RequiredItems`, `ShapeGeometry`, `ShapeMeshes`
  (Wicklung gegen Godots BoxMesh, UV2 ∈ [0,1]), `ShapeCoverage` (50 Seeds × 100 Chunks), Drop-Tabelle 27/27/22/12/12.

## 3. Hinweise für VISUALS (Mesh-Attribute, §4.3)
- Alle Nicht-Box-Formen (auch `Square`) laufen über `ShapeMeshFactory` + `MaterialLibrary.GetShapeVariant(name)`
  → `art/materials/{name}_shape.tres`. Genutzte Basisnamen: `platform_gap`, `platform_stairs`, `platform_zigzag`,
  `platform_moving`, `platform_height`, `platform_pillars`, `platform_spin`, `platform_shapes`.
- Abweichung/Präzisierung: Der Mantel von **Cylinder/Disc** (32 Segmente) hat weiche Normalen und `UV2.x = 0.5`
  (UV2.y 0 unten / 1 oben), damit keine Segmentnähte als Kanten erscheinen. Alle anderen Seitenflächen: Quad (0,0)…(1,1).
- Deckel/Böden sind Fächer: Rand `UV2 = (0.5, 0)`, Mitte `(0.5, 1)`. Mit der Formel
  `min(UV2.x, 1-UV2.x, UV2.y, 1-UV2.y)` entsteht genau im Mittelpunkt ein pixelgroßer Punkt (1−y = 0). Empfehlung:
  auf Deckflächen (`NORMAL·UP > 0.5`) bzw. generell bei `UV2.x == 0.5` nur `UV2.y` als Kantenmaß verwenden, z. B.
  `float e = (abs(UV2.x - 0.5) < 0.001) ? UV2.y : min(min(UV2.x, 1.0 - UV2.x), min(UV2.y, 1.0 - UV2.y));`
- `UV` = planar in Mesh-Einheiten (Einheitsmesh, vor Node-Scale); Weltkoordinaten im Shader sind ggf. robuster.
- Rotor-Szene `scenes/objects/HelicopterRotor.tscn` nutzt `helicopter_rotor` (Blätter) und `helicopter_hub` (Mast/Nabe),
  Pickup nutzt `pickup_helicopter`. Fehlen sie, greift der Magenta-Fallback (mit einmaliger Warnung).

## 4. Hinweise für AUDIO / CHARACTERS
- Signale exakt wie §5.3 (`Attacked`, `ItemUsed(int)`, `GrappleStarted`, `GrappleEnded`, `FlightStarted`, `FlightEnded`,
  `Bounced`), `Inventory.ItemAdded(int, int)`, `Inventory.InventoryChanged` hat jetzt 4 Argumente.
- `PlayerController.ModelTopHeight` setzt die Rotorhöhe sofort; `ActivateItem(ItemType)` ist öffentlich (Tests).
- `Visual/Body`/`Visual/Visor` werden nur noch null-sicher eingefärbt (dürfen entfallen).

## 5. Für DOCS
- Formen + Muster: siehe `scripts/World/PlatformShape.cs`, `ChunkBuilder.BuildPillars/BuildSpinning/BuildShapeMix`.
- Formvariation bestehender Muster: GapJump (Box/Square/Disc/Hexagon/Octagon/Triangle), Stairs (Box/Square/Cylinder/
  Ramp – Rampen nur aufwärts), ZigZag (Box/Disc/Hexagon/Diamond/Square), HeightJump (Box/Cylinder/Hexagon/Octagon),
  MovingPlatforms (Box/Disc/Hexagon). Start, NarrowBeams, Pad, Landing, BigGap, TallWall bleiben Boxen.
- Start-Chunk hat eine dritte Tutorial-Kiste mit Helikopter bei (0, 0.5, −26).
